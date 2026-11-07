using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.PhoneOtp;
using Skopka.Identity.Users;

namespace Skopka.Hello;

/// <summary>A protected, session-bound request to attach a verified phone.</summary>
public sealed record HelloPhoneBindingChallenge(string Ticket, TimeSpan RetryAfter);

/// <summary>Starts and completes phone binding for an online authenticated session.</summary>
public interface IHelloPhoneBindingApplication<TProfile>
{
    /// <summary>Sends a code to the proposed phone without disclosing whether it is already in use.</summary>
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken,
        string phone, string? clientKey, CancellationToken cancellationToken);
    /// <summary>Verifies the code and binds the phone to the authenticated account.</summary>
    Task<OperationResult<HelloAccount<TProfile>>> CompleteAsync(string accessToken,
        string ticket, string code, string? clientKey, CancellationToken cancellationToken);
}

internal sealed class HelloPhoneBindingApplication<TProfile>(
    IHelloPhoneOtpApplication<TProfile> sender,
    IIdentityPhoneOtpService<TProfile> phoneOtp,
    IEnumerable<IHelloAccessTokenValidator<TProfile>> validators,
    HelloPhoneInputPolicy phonePolicy,
    IDataProtectionProvider protection,
    TimeProvider clock, IHelloAccountPolicy<TProfile>? accountPolicy = null,
    IHelloSiteContext? sites = null) : IHelloPhoneBindingApplication<TProfile>
{
    private readonly IDataProtector protector = protection.CreateProtector("Skopka.Hello.PhoneBinding.v1");
    private bool SiteEnabled => sites is null || sites.Current is { PhoneOtpEnabled: true } site
        && site.IsEnabled(HelloSiteFeatures.Account);
    private sealed record Ticket(Guid UserId, string TokenHash, long Version, string Phone,
        Guid ChallengeId, DateTimeOffset ExpiresAt);
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static Error Invalid() => new(IdentityErrorCodes.VerificationProofInvalid,
        "Phone binding request is invalid or expired.", ErrorType.Unauthorized);

    private async Task<OperationResult<IdentityUser<TProfile>>> ValidateAsync(string token, CancellationToken ct)
    {
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(token, ct);
            if (result.IsSuccess) return result;
        }
        return OperationResultFactory.Fail<IdentityUser<TProfile>>(Invalid());
    }

    public async Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken,
        string phone, string? clientKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(Invalid());
        if (!SiteEnabled) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(HelloSiteAccess.Unavailable());
        var user = await ValidateAsync(accessToken, cancellationToken);
        if (!user.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(user.Errors);
        var permission = await HelloAccountPolicy.RequireEditAsync(accountPolicy, user.Value, HelloAccountArea.Phone, cancellationToken);
        if (!permission.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(permission.Errors);
        var normalized = phonePolicy.Normalize(phone, otpRequest: true);
        if (!normalized.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(normalized.Errors);
        var issued = await sender.RequestCodeAsync(normalized.Value, clientKey, null, cancellationToken);
        if (!issued.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(issued.Errors);
        var ticket = new Ticket(user.Value.Id, Hash(accessToken), user.Value.Version, normalized.Value,
            issued.Value.RequestId, clock.GetUtcNow().AddMinutes(5));
        return OperationResultFactory.Success(new HelloPhoneBindingChallenge(
            protector.Protect(JsonSerializer.Serialize(ticket)), issued.Value.RetryAfter));
    }

    public async Task<OperationResult<HelloAccount<TProfile>>> CompleteAsync(string accessToken,
        string ticket, string code, string? clientKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(ticket) || ticket.Length > 8192)
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
        if (!SiteEnabled) return OperationResultFactory.Fail<HelloAccount<TProfile>>(HelloSiteAccess.Unavailable());
        Ticket? pending;
        try { pending = JsonSerializer.Deserialize<Ticket>(protector.Unprotect(ticket)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid()); }
        var user = await ValidateAsync(accessToken, cancellationToken);
        if (!user.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(user.Errors);
        var permission = await HelloAccountPolicy.RequireEditAsync(accountPolicy, user.Value, HelloAccountArea.Phone, cancellationToken);
        if (!permission.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(permission.Errors);
        if (pending is null || pending.ExpiresAt <= clock.GetUtcNow() || pending.UserId != user.Value.Id
            || pending.Version != user.Value.Version || pending.TokenHash != Hash(accessToken))
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
        var verified = await phoneOtp.VerifyForBindingAsync(new VerifyPhoneOtpCommand(
            pending.ChallengeId, code, clientKey), cancellationToken);
        if (!verified.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(verified.Errors);
        var result = await phoneOtp.BindVerifiedPhoneAsync(new BindVerifiedPhoneCommand(
            user.Value.Id, pending.Version, pending.Phone, verified.Value), cancellationToken);
        if (!result.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(result.Errors);
        var value = result.Value;
        return OperationResultFactory.Success(new HelloAccount<TProfile>(value.Id, value.Flags, value.UserName,
            value.Email, value.EmailConfirmed, value.Phone, value.PhoneConfirmed, value.Profile,
            value.Version, value.CreatedAt, value.ModifiedAt));
    }
}
