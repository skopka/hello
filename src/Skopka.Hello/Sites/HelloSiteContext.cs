using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.Authentication;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello;

/// <summary>Resolves only configured origins. An unknown origin has no current site.</summary>
public interface IHelloSiteContext
{
    HelloSite? Current { get; }
    HelloSite? Find(string siteId);
}

internal sealed class HelloSiteCatalog
{
    internal bool MultipleOrigins { get; }
    internal IReadOnlyList<HelloSite> Sites { get; }

    public HelloSiteCatalog(SkopkaHelloOptions options)
    {
        MultipleOrigins = options.Sites.Count != 0;
        Sites = options.Sites.Count == 0
            ? [new("default", options.PublicOrigin, options.PasswordSignInEnabled,
                options.PhoneOtpEnabled, options.SelfRegistrationEnabled, true, true, true,
                options.PasswordLoginHandle, HelloSiteFeatures.All, null, null, null)]
            : options.Sites.Select(site => new HelloSite(site.Id, site.PublicOrigin,
                options.PasswordSignInEnabled && site.PasswordSignInEnabled,
                options.PhoneOtpEnabled && site.PhoneOtpEnabled,
                options.SelfRegistrationEnabled && site.SelfRegistrationEnabled,
                site.ExternalSignInEnabled, site.WebAuthnSignInEnabled, site.CrossDeviceSignInEnabled,
                site.PasswordLoginHandle ?? options.PasswordLoginHandle, site.EnabledFeatures,
                site.ApplicationHomeUrl, site.PostLogoutRedirectPath, site.AuthenticatedRedirectPath)
                { PhoneScope = site.PhoneScope }).ToArray();
    }
}

internal sealed class HelloSiteContext(HelloSiteCatalog catalog, IHttpContextAccessor http) : IHelloSiteContext
{
    public HelloSite? Current
    {
        get
        {
            if (!catalog.MultipleOrigins) return catalog.Sites[0];
            var request = http.HttpContext?.Request;
            if (request is null || !request.IsHttps) return null;
            return catalog.Sites.FirstOrDefault(site => string.Equals(
                site.PublicOrigin!.IdnHost, request.Host.Host, StringComparison.OrdinalIgnoreCase)
                && site.PublicOrigin.Port == (request.Host.Port ?? 443));
        }
    }

    public HelloSite? Find(string siteId) => catalog.Sites.FirstOrDefault(site => site.Id == siteId);
}

internal static class HelloSiteAccess
{
    internal static Error Denied() => new(IdentityErrorCodes.InvalidCredentials,
        "Unable to sign in with these credentials on this site.", ErrorType.Unauthorized);
    internal static Error Unavailable() => new("hello.site.unavailable",
        "This operation is unavailable on this site.", ErrorType.Forbidden);

    internal static bool Allows(IHelloSiteContext? context, HelloSiteFeatures features)
        => context is null || context.Current?.IsEnabled(features) == true;

    /// <summary>Phone scope of the current site; <see langword="null"/> without sites or a configured scope.</summary>
    internal static string? PhoneScope(IHelloSiteContext? context) => context?.Current?.PhoneScope;

    /// <summary>
    /// Scope in which an entered number is looked up at password sign-in. Never
    /// <see langword="null"/>: for <see cref="PasswordLoginHandle.Automatic"/> Identity
    /// treats <see langword="null"/> as "every scope", where a number shared by a personal
    /// and a staff account is ambiguous, so a site without a scope passes the default
    /// scope explicitly. Without named scopes every number is in the default scope, so
    /// hosts that use none see no change.
    /// </summary>
    internal static string SignInPhoneScope(IHelloSiteContext? context)
        => context?.Current?.PhoneScope ?? IdentityPhoneScopes.Default;

    internal static IdentitySessionContext? Session(IHelloSiteContext? context, string method)
        => context?.Current is { } site ? new(site.Id, method) : null;

    internal static bool AllowsMethod(HelloSite site, string method) => site.IsEnabled(HelloSiteFeatures.Login)
        && method switch
        {
            HelloAuthenticationMethods.Password => site.PasswordSignInEnabled,
            HelloAuthenticationMethods.Phone => site.PhoneOtpEnabled,
            HelloAuthenticationMethods.External => site.ExternalSignInEnabled && site.IsEnabled(HelloSiteFeatures.ExternalIdentity),
            HelloAuthenticationMethods.Passkey => site.WebAuthnSignInEnabled,
            HelloAuthenticationMethods.AccessLink => site.PasswordSignInEnabled,
            _ => false,
        };
}

/// <summary>Online account and immutable session context presented to host sign-in rules.</summary>
public sealed record HelloSignInContext<TProfile>(HelloAccount<TProfile> Account, HelloSite Site,
    string AuthenticationMethod, IdentitySessionAdmissionOperation Operation);

/// <summary>Checked on session creation, refresh and online validation. Denials use a common credential error.</summary>
public interface IHelloSignInPolicy<TProfile>
{
    Task<bool> IsAllowedAsync(HelloSignInContext<TProfile> context, CancellationToken cancellationToken);
}

internal sealed class HelloSiteSessionAdmission<TProfile>(IHelloSiteContext sites,
    HelloSiteCatalog catalog, IEnumerable<IHelloSignInPolicy<TProfile>> policies)
    : IIdentitySessionAdmissionPolicy<TProfile>
{
    public async Task<OperationResult> CheckAsync(IdentitySessionAdmissionContext<TProfile> context, CancellationToken ct)
    {
        var site = sites.Current;
        if (site is null || context.Context is { } session && session.Scope != site.Id
            || catalog.MultipleOrigins && context.Context is null)
            return OperationResultFactory.Fail(HelloSiteAccess.Denied());
        var method = context.Context?.AuthenticationMethod ?? "legacy";
        if (context.Context is not null && !HelloSiteAccess.AllowsMethod(site, method))
            return OperationResultFactory.Fail(HelloSiteAccess.Denied());
        var account = HelloIdentityApplication<TProfile>.ToAccount(context.User);
        foreach (var policy in policies)
            if (!await policy.IsAllowedAsync(new(account, site, method, context.Operation), ct))
                return OperationResultFactory.Fail(HelloSiteAccess.Denied());
        return OperationResultFactory.Success();
    }
}
