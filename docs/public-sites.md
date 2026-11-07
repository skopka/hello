# Public sites and account admission

Hello 0.29.0 requires Skopka.Identity 0.18.0 for these features.

One process can expose multiple explicitly configured HTTPS origins. Define
`SkopkaHelloOptions.Sites` once at startup. Site identifiers are stable security
identifiers: changing one invalidates access to sessions issued under the old id.
The global Hello switches and global UI page groups remain upper bounds. When
`Sites` is empty, the existing single-site configuration remains supported.

The following configuration fragment enables phone sign-in on the personal site
and username/password sign-in on the staff site. Both sites still need the host's
normal Identity persistence, token, password, TOTP and delivery registrations.

```csharp
var identity = services.AddSkopkaHello<Profile>(options =>
{
    options.PhoneOtpEnabled = true;
    options.Totp.Enabled = true;
    options.Sites.Add(new HelloSiteOptions
    {
        Id = "personal",
        PublicOrigin = new Uri("https://iqzone.example"),
        PasswordLoginHandle = PasswordLoginHandle.UserName,
        ApplicationHomeUrl = "/",
        AuthenticatedRedirectPath = "/learning",
        PostLogoutRedirectPath = "/"
    });
    options.Sites.Add(new HelloSiteOptions
    {
        Id = "staff",
        PublicOrigin = new Uri("https://staff.iqzone.example"),
        PasswordLoginHandle = PasswordLoginHandle.UserName,
        PhoneOtpEnabled = false,
        SelfRegistrationEnabled = false,
        ExternalSignInEnabled = false,
        WebAuthnSignInEnabled = false,
        CrossDeviceSignInEnabled = false,
        EnabledFeatures = HelloSiteFeatures.Login | HelloSiteFeatures.Account
            | HelloSiteFeatures.Sessions | HelloSiteFeatures.AccountSecurity,
        ApplicationHomeUrl = "/work",
        AuthenticatedRedirectPath = "/work",
        PostLogoutRedirectPath = "/signed-out"
    });
});
```

`PasswordLoginHandle` is in `Skopka.Identity.Authentication`. A site can enable
`PasswordRecovery` or `ContactConfirmation` when it uses those contact workflows.
Disabled features and methods are also rejected on the corresponding API routes
and direct Razor handlers. Home, authenticated-return and logout destinations
are local paths. Cookies remain host-only; do not add a shared parent Domain in
host authentication configuration. HTTPS ports distinguish configured origins,
but cookies are not isolated by port: use different hostnames for isolated tabs.

`IHelloSiteContext.Current` resolves only configured HTTPS hosts. Unknown hosts
are rejected. Behind a proxy, configure trusted forwarded headers before Hello;
never trust arbitrary client-supplied forwarding headers. Do not derive public
action URLs from an unchecked `Host` header. Queued password-reset/confirmation
requests retain the configured site id, so their background worker builds the
link on the original origin. OIDC callbacks use that origin and protected flows
carry the site id. Register each callback URL with the external provider.

## Which account can sign in

Register `IHelloSignInPolicy<Profile>` as a scoped service. Its
`HelloSignInContext<Profile>` contains the current account, configured site,
original authentication method and the Identity admission operation. It runs
when creating or registering a session, refreshing it and validating it online.
Return `false` for a forbidden combination. Hello maps it to the common credential
failure, including in the UI; it does not tell an anonymous caller that an
account exists. The policy must not inject the Hello application itself, which
would create a circular dependency; query host data through independent services.

Persisted `IdentitySessionContext` is separate from display-only device metadata.
A refresh token cannot choose a new site or method. Second-factor, external
credential rotation, authorization-code redemption and device/native handoff
retain the originating context. A host opting into explicit sites must migrate
by requiring a fresh sign-in for legacy sessions without context. Direct trusted
Identity session creation must supply the same server-derived context; never
accept it from a request body.

For example, the policy can admit `Staff` only to `staff` with
`HelloAuthenticationMethods.Password`, `Child` only to `personal` with password,
and personal accounts only to `personal` with phone. The compiled sample also
admits a child with a confirmed phone through phone sign-in. Separately configure the
existing mandatory-second-factor policy for staff. A site method switch alone
does not require TOTP and does not classify accounts.

## Account-specific lifetime

Register `IIdentitySessionLifetimePolicy<Profile>` (namespace
`Skopka.Identity.Sessions`). `GetLifetimeAsync` receives the validated user,
trusted original session context and configured default duration. Return a
positive bounded duration, for example 180 days for a child, 8 hours for staff
and `defaultLifetime` for other accounts. The policy sets the absolute expiry at
session creation; refresh never extends it. Changing the policy does not rewrite
already-issued expirations. Revoke old sessions when a shorter limit must apply
immediately. Browser cookie and API token lifetimes cannot extend the underlying
logical session.

Account editing and registration continuation are separate host decisions: see
[account policy](account-policy.md), [registration continuation](registration-continuation.md),
[access links](access-links.md) and [host SMS](host-sms.md).
