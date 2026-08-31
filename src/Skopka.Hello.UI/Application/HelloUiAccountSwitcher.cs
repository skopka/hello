using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

internal sealed class HelloUiAccountSwitcher<TProfile>(
    IHelloIdentityApplication<TProfile> application,
    IHelloUiProfileFactory<TProfile> profiles,
    IDataProtectionProvider dataProtection,
    SkopkaHelloUiOptions options)
    : IHelloUiAccountSwitcher
{
    private const int MaximumProtectedCookieLength = 3800;
    private readonly IDataProtector protector = dataProtection.CreateProtector(
        "Skopka.Hello.UI.AccountSwitching.v1");

    public IReadOnlyList<HelloUiSavedAccount> List(
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!options.AccountSwitching.Enabled)
        {
            return [];
        }

        HelloUiPrincipalFactory.TryGetUserId(
            httpContext.User,
            out var currentUserId);
        return Read(httpContext)
            .OrderByDescending(account => account.LastUsedAt)
            .Select(account => new HelloUiSavedAccount(
                account.UserId,
                account.SessionId,
                account.DisplayName,
                account.UserName,
                account.Email,
                account.RefreshTokenExpiresAt,
                account.UserId == currentUserId))
            .ToArray();
    }

    public void Save(
        HttpContext httpContext,
        ClaimsPrincipal principal,
        HelloSession session)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(session);
        if (!options.AccountSwitching.Enabled
            || !HelloUiPrincipalFactory.TryGetUserId(
                principal,
                out var userId))
        {
            return;
        }

        var accounts = Read(httpContext)
            .Where(account => account.UserId != userId)
            .ToList();
        accounts.Add(new SavedAccount(
            userId,
            session.SessionId,
            Limit(principal.FindFirstValue(
                HelloUiPrincipalFactory.DisplayNameClaim), 160)
                ?? "Account",
            Limit(principal.FindFirstValue(ClaimTypes.Name), 100),
            Limit(principal.FindFirstValue(ClaimTypes.Email), 320),
            session.RefreshToken,
            session.RefreshTokenExpiresAt,
            DateTimeOffset.UtcNow));
        Write(httpContext, accounts, userId);
    }

    public async Task<OperationResult<HelloUiSignIn>> SwitchAsync(
        HttpContext httpContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var account = Read(httpContext)
            .FirstOrDefault(candidate => candidate.UserId == userId);
        if (account is null)
        {
            return MissingAccount();
        }

        var refreshed = await application.RefreshAsync(
            account.RefreshToken,
            cancellationToken);
        if (!refreshed.IsSuccess)
        {
            Remove(httpContext, account.UserId);
            return OperationResultFactory.Fail<HelloUiSignIn>(
                refreshed.Errors);
        }

        var validated = await application.ValidateAccessTokenAsync(
            refreshed.Value.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            Remove(httpContext, account.UserId);
            return OperationResultFactory.Fail<HelloUiSignIn>(
                validated.Errors);
        }

        return OperationResultFactory.Success(
            new HelloUiSignIn(
                HelloUiPrincipalFactory.Create(
                    validated.Value,
                    refreshed.Value.SessionId,
                    profiles),
                refreshed.Value));
    }

    public async Task<OperationResult> RemoveAsync(
        HttpContext httpContext,
        Guid userId,
        bool revokeSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var account = Read(httpContext)
            .FirstOrDefault(candidate => candidate.UserId == userId);
        if (account is null)
        {
            return OperationResultFactory.Success();
        }

        OperationResult result = OperationResultFactory.Success();
        if (revokeSession)
        {
            result = await application.LogoutAsync(
                account.RefreshToken,
                cancellationToken);
        }

        Remove(httpContext, userId);
        return result;
    }

    public void RemoveSession(HttpContext httpContext, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!options.AccountSwitching.Enabled)
        {
            return;
        }

        var accounts = Read(httpContext)
            .Where(account => account.SessionId != sessionId)
            .ToList();
        Write(httpContext, accounts, preferredUserId: null);
    }

    private void Remove(HttpContext httpContext, Guid userId)
    {
        var accounts = Read(httpContext)
            .Where(account => account.UserId != userId)
            .ToList();
        Write(httpContext, accounts, preferredUserId: null);
    }

    private List<SavedAccount> Read(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(
                options.AccountSwitching.CookieName,
                out var value)
            || string.IsNullOrWhiteSpace(value)
            || value.Length > 16384)
        {
            return [];
        }

        try
        {
            var payload = protector.Unprotect(value);
            return JsonSerializer.Deserialize<List<SavedAccount>>(payload)
                    ?.Where(IsValid)
                    .Where(account =>
                        account.RefreshTokenExpiresAt > DateTimeOffset.UtcNow)
                    .GroupBy(account => account.UserId)
                    .Select(group => group.MaxBy(account => account.LastUsedAt)!)
                    .OrderByDescending(account => account.LastUsedAt)
                    .Take(options.AccountSwitching.MaximumSavedAccounts)
                    .ToList()
                ?? [];
        }
        catch (Exception exception)
            when (exception is CryptographicException
                or JsonException
                or FormatException)
        {
            return [];
        }
    }

    private void Write(
        HttpContext httpContext,
        List<SavedAccount> accounts,
        Guid? preferredUserId)
    {
        if (!options.AccountSwitching.Enabled)
        {
            return;
        }

        accounts = accounts
            .Where(IsValid)
            .Where(account =>
                account.RefreshTokenExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(account =>
                account.UserId == preferredUserId)
            .ThenByDescending(account => account.LastUsedAt)
            .Take(options.AccountSwitching.MaximumSavedAccounts)
            .ToList();
        if (accounts.Count == 0)
        {
            httpContext.Response.Cookies.Delete(
                options.AccountSwitching.CookieName,
                CreateCookieOptions(expires: null));
            return;
        }

        string protectedPayload;
        while (true)
        {
            protectedPayload = protector.Protect(
                JsonSerializer.Serialize(accounts));
            if (protectedPayload.Length <= MaximumProtectedCookieLength
                || accounts.Count == 1)
            {
                break;
            }

            accounts.RemoveAt(accounts.Count - 1);
        }

        httpContext.Response.Cookies.Append(
            options.AccountSwitching.CookieName,
            protectedPayload,
            CreateCookieOptions(accounts.Max(account =>
                account.RefreshTokenExpiresAt)));
    }

    private CookieOptions CreateCookieOptions(DateTimeOffset? expires)
        => new()
        {
            HttpOnly = true,
            Secure = options.SecureCookies,
            IsEssential = true,
            SameSite = options.CookieSameSite,
            Path = "/",
            Expires = expires,
        };

    private static bool IsValid(SavedAccount account)
        => account.UserId != Guid.Empty
            && account.SessionId != Guid.Empty
            && !string.IsNullOrWhiteSpace(account.DisplayName)
            && account.DisplayName.Length <= 160
            && (account.UserName?.Length ?? 0) <= 100
            && (account.Email?.Length ?? 0) <= 320
            && !string.IsNullOrWhiteSpace(account.RefreshToken)
            && account.RefreshToken.Length <= 1024;

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= maximumLength
            ? value
            : value[..maximumLength];
    }

    private static OperationResult<HelloUiSignIn> MissingAccount()
        => OperationResultFactory.Fail<HelloUiSignIn>(
            new Error(
                "hello.account_switching.account_unavailable",
                "The saved account is unavailable or expired.",
                ErrorType.Unauthorized));

    private sealed record SavedAccount(
        Guid UserId,
        Guid SessionId,
        string DisplayName,
        string? UserName,
        string? Email,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        DateTimeOffset LastUsedAt);
}
