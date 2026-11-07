using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.WebAuthn;

namespace Skopka.Hello;

/// <summary>Marks one exact endpoint as accessible while onboarding is pending.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HelloContinuationExemptAttribute : Attribute;

/// <summary>Options for the host-installed registration continuation gate.</summary>
public sealed class HelloContinuationGateOptions
{
    /// <summary>A local host-owned continuation page path.</summary>
    public string ContinuationPath { get; set; } = "/onboarding";
    /// <summary>Authentication scheme containing the protected UI access token.</summary>
    public IList<string> AuthenticationSchemes { get; } = ["Skopka.Hello.UI"];
    /// <summary>Fallback local path after completion when no original return was recorded.</summary>
    public string DefaultReturnPath { get; set; } = "/";
}

/// <summary>Installs a live onboarding gate after routing and authentication.</summary>
public static class HelloContinuationGateExtensions
{
    /// <summary>All protected host and Hello routes must pass through this middleware.</summary>
    public static IApplicationBuilder UseHelloRegistrationContinuation<TProfile>(
        this IApplicationBuilder app, HelloContinuationGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);
        if (!HelloLocalReturnUrl.IsLocal(options.ContinuationPath)
            || !HelloLocalReturnUrl.IsLocal(options.DefaultReturnPath))
            throw new InvalidOperationException("Continuation and fallback paths must be local.");
        return app.UseMiddleware<HelloContinuationGate<TProfile>>(options);
    }
}

/// <summary>Checks pending status and manages the protected return cookie.</summary>
public sealed class HelloContinuationGate<TProfile>(RequestDelegate next, HelloContinuationGateOptions options)
{
    private const string CookieName = "__Host-Skopka.Hello.Continuation";
    private sealed record ReturnState(Guid FlowId, Guid UserId, string ReturnUrl, DateTimeOffset ExpiresAt);

    /// <summary>Evaluates every authenticated request except explicitly marked endpoints.</summary>
    public async Task InvokeAsync(HttpContext context, IHelloIdentityApplication<TProfile> identity,
        IHelloRegistrationContinuation<TProfile> continuation, IDataProtectionProvider protection)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<HelloContinuationExemptAttribute>() is not null)
        {
            await next(context);
            return;
        }
        var hasBearer = context.Request.Headers.Authorization.ToString()
            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Request.Headers.Authorization.ToString(), "Bearer", StringComparison.OrdinalIgnoreCase);
        var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        foreach (var name in options.AuthenticationSchemes)
        {
            if (await schemeProvider.GetSchemeAsync(name) is null) continue;
            if ((await context.AuthenticateAsync(name)).Succeeded) authenticated = true;
        }
        if (!hasBearer && !authenticated)
        {
            await next(context);
            return;
        }
        var account = await ResolveAsync(context, identity, options, context.RequestAborted);
        if (!account.IsSuccess)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        var pending = await continuation.IsPendingAsync(account.Value, context.RequestAborted);
        if (!pending.IsSuccess)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        if (!pending.Value)
        {
            await next(context);
            return;
        }
        if (IsApi(context))
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Registration continuation required",
                extensions: new Dictionary<string, object?> { ["code"] = "hello.registration.continuation_required" })
                .ExecuteAsync(context);
            return;
        }
        var protector = protection.CreateProtector("Skopka.Hello.Continuation.Return.v1");
        var prior = ReadState(context, protector);
        if ((prior is null || prior.UserId != account.Value.Id) && IsDocumentNavigation(context))
        {
            var url = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            if (HelloLocalReturnUrl.IsLocal(url))
                context.Response.Cookies.Append(CookieName, protector.Protect(JsonSerializer.Serialize(
                    new ReturnState(Guid.NewGuid(), account.Value.Id, url, DateTimeOffset.UtcNow.AddHours(1)))), CookieOptions());
        }
        context.Response.Redirect(options.ContinuationPath);
    }

    /// <summary>Returns and clears the stored local destination only after the host reports completion.</summary>
    internal static async Task<OperationResult<string>> CompleteCoreAsync(HttpContext context,
        IHelloIdentityApplication<TProfile> identity, IHelloRegistrationContinuation<TProfile> continuation,
        IDataProtectionProvider protection, HelloContinuationGateOptions options, CancellationToken cancellationToken)
    {
        var account = await ResolveAsync(context, identity, options, cancellationToken);
        if (!account.IsSuccess) return OperationResultFactory.Fail<string>(account.Errors);
        var pending = await continuation.IsPendingAsync(account.Value, cancellationToken);
        if (!pending.IsSuccess) return OperationResultFactory.Fail<string>(pending.Errors);
        if (pending.Value) return OperationResultFactory.Fail<string>(new Error(
            "hello.registration.continuation_required", "Registration continuation is incomplete.", ErrorType.Forbidden));
        var state = ReadState(context, protection.CreateProtector("Skopka.Hello.Continuation.Return.v1"));
        context.Response.Cookies.Delete(CookieName, CookieOptions());
        if (state is null || state.UserId != account.Value.Id)
            return OperationResultFactory.Success(options.DefaultReturnPath);
        var flowStore = context.RequestServices.GetRequiredService<IHelloWebAuthnFlowStore>();
        if (!await flowStore.TryConsumeAsync(state.FlowId, state.ExpiresAt, cancellationToken))
            return OperationResultFactory.Fail<string>(new Error(
                "hello.registration.continuation_consumed", "The continuation has already been used.", ErrorType.Forbidden));
        return OperationResultFactory.Success(state.ReturnUrl);
    }

    private static async Task<OperationResult<HelloAccount<TProfile>>> ResolveAsync(HttpContext context,
        IHelloIdentityApplication<TProfile> identity, HelloContinuationGateOptions options, CancellationToken ct)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        var bearer = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(auth, "Bearer", StringComparison.OrdinalIgnoreCase);
        var token = bearer && auth.Length > 6 ? auth[7..].Trim() : null;
        if (!bearer)
        {
            var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            foreach (var name in options.AuthenticationSchemes)
            {
                if (await schemes.GetSchemeAsync(name) is null) continue;
                var ticket = await context.AuthenticateAsync(name);
                if (!ticket.Succeeded) continue;
                token = ticket.Properties?.GetTokenValue("access_token");
                if (!string.IsNullOrWhiteSpace(token)) break;
            }
        }
        return string.IsNullOrWhiteSpace(token)
            ? OperationResultFactory.Fail<HelloAccount<TProfile>>(new Error("hello.session.invalid", "Session is invalid.", ErrorType.Unauthorized))
            : await identity.ValidateAccessTokenAsync(token, ct);
    }

    private static bool IsDocumentNavigation(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && (string.Equals(context.Request.Headers["Sec-Fetch-Dest"].ToString(), "document", StringComparison.OrdinalIgnoreCase)
            || context.Request.GetTypedHeaders().Accept?.Any(x => x.MediaType.Value == "text/html") == true);

    private static bool IsApi(HttpContext context) =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        || context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true
        || (!HttpMethods.IsGet(context.Request.Method) && !context.Request.HasFormContentType)
        || context.Request.GetTypedHeaders().Accept?.Any(x => x.MediaType.Value == "application/json") == true;

    private static CookieOptions CookieOptions() => new()
    { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromHours(1) };

    private static ReturnState? ReadState(HttpContext context, IDataProtector protector)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || value.Length > 8192) return null;
        try
        {
            var state = JsonSerializer.Deserialize<ReturnState>(protector.Unprotect(value));
            return state is not null && state.ExpiresAt > DateTimeOffset.UtcNow && HelloLocalReturnUrl.IsLocal(state.ReturnUrl)
                ? state : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { return null; }
    }
}

/// <summary>Completes a host-owned continuation after its durable state has changed.</summary>
public static class HelloContinuationCompletion
{
    /// <summary>Returns the saved local destination only when onboarding is no longer pending.</summary>
    public static Task<OperationResult<string>> CompleteAsync<TProfile>(HttpContext context,
        IHelloIdentityApplication<TProfile> identity, IHelloRegistrationContinuation<TProfile> continuation,
        IDataProtectionProvider protection, HelloContinuationGateOptions options, CancellationToken cancellationToken)
        => HelloContinuationGate<TProfile>.CompleteCoreAsync(context, identity, continuation, protection, options, cancellationToken);
}
