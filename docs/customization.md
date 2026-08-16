# Customization

## Profile type

Define one JSON-serializable profile type for the host:

```csharp
public sealed record MyProfile(string DisplayName, string? Locale);
```

`RegisterRequest<TProfile>` binds its `profile` property directly to this type.
Profile schema changes are application data migrations. Do not place ASP.NET,
EF, credential or protocol types in a profile.

If the profile stores registration-consent evidence, use a host-owned value
type and map the trusted Hello evidence into it. Do not trust similarly named
values inside the client-supplied `profile` JSON.

## Hello options

```csharp
services.AddSkopkaHello<MyProfile>(options =>
{
    options.ClientName = "my-web-client";
    options.CookieSameSite = SameSiteMode.Strict;
    options.SelfRegistrationEnabled = false;
    options.UiPathPrefix = "/accounts";
    options.RegistrationConsent.TermsOfServiceRequired = true;
    options.RegistrationConsent.PrivacyPolicyRequired = true;
});
```

Both settings are fixed when `AddSkopkaHello<TProfile>` runs. The registration
flag covers built-in password and external self-registration, but does not
prevent trusted host code or a future admin workflow from calling
Skopka.Identity registration services directly. `UiPathPrefix` moves only the
Hello Razor routes; it is not a global ASP.NET Core `PathBase`. It must be a
non-empty absolute prefix other than `/` so UI routes cannot collide with the
root-relative APIs.

The default `__Host-` cookie names require secure cookies. For an explicit
plain-HTTP local test only, change all three cookie names to non-`__Host-` names
before disabling `SecureCookies`.

Replace `IHelloRequestContext` to provide a trusted gateway/device context. The
implementation must not accept a body-provided client key or put raw IP
addresses in session display metadata.

## Security events

Register `IHelloSecurityEventSink` before `AddSkopkaHello<TProfile>()` to receive
safe, enriched post-commit events:

```csharp
services.AddSingleton<IHelloSecurityEventSink, MyEventSink>();
services.AddSkopkaHello<MyProfile>();
```

Both self-service and admin user deletion produce
`IdentitySecurityEventTypes.UserDeleted`; the deleted user id is
`SubjectUserId`. `DeliveryStage` is explicitly
`AfterIdentityCommit`, so a sink failure cannot roll the deletion back. Use
`EventId` as an idempotency key and synchronously write a small record to the
host's durable queue/outbox; anonymize the platform data in a worker.

```csharp
public OperationResult Write(HelloSecurityEventEnvelope securityEvent)
{
    if (securityEvent.EventType
            != IdentitySecurityEventTypes.UserDeleted
        || securityEvent.SubjectUserId is not { } userId)
    {
        return OperationResultFactory.Success();
    }

    Debug.Assert(
        securityEvent.DeliveryStage
            == HelloSecurityEventDeliveryStage.AfterIdentityCommit);
    return deletionOutbox.Enqueue(
        securityEvent.EventId,
        userId);
}
```

The sink returns `OperationResult`, must return quickly and must not throw. It
is a post-commit notification, not a transactional participant. A common
transaction with platform tables can only be provided by a host-owned outer
transaction/store integration; Hello does not claim that boundary.

The ready Server replaces the no-op sink with a PostgreSQL post-commit audit
outbox when `SkopkaHello:Persistence:AuditEnabled` is true. That makes each
successfully inserted record restart-safe, but it cannot make an observer write
atomic with the Identity transaction that already committed.

## UI and styles

Register the Razor Class Library with an application-specific profile factory:

```csharp
services.AddSkopkaHelloUi<MyProfile, MyProfileUiFactory>(options =>
{
    options.CustomCssFilePath = "/themes/custom.css";
    options.Registration.Email =
        HelloUiRegistrationFieldMode.Required;
    options.Registration.UserName =
        HelloUiRegistrationFieldMode.Hidden;
    options.Registration.Phone =
        HelloUiRegistrationFieldMode.Hidden;
});

app.MapStaticAssets();
app.MapSkopkaHelloUi();
```

The packaged pages set their layout from
`Pages/SkopkaHello/_ViewStart.cshtml` and use the absolute path
`/Pages/Shared/_SkopkaHelloLayout.cshtml`. A host's ordinary
`Pages/_ViewStart.cshtml` and `Pages/Shared/_Layout.cshtml` therefore do not
replace the Hello shell. To replace it deliberately, add
`Pages/Shared/_SkopkaHelloLayout.cshtml` to the host with the same path and
provide the required body, navigation, logout and localization UI there.

### Registration fields

Each built-in registration field has one of three modes:
`Hidden`, `Optional` or `Required`. The setting controls both password and
external Razor registration, including server-side validation. Hidden fields
are removed from the submitted model, so a caller cannot add them by crafting
a POST request.

```csharp
services.AddSkopkaHelloUi<MyProfile, MyProfileUiFactory>(options =>
{
    // Phone-only local identity. Display name and password remain required.
    options.Registration.Email =
        HelloUiRegistrationFieldMode.Hidden;
    options.Registration.UserName =
        HelloUiRegistrationFieldMode.Hidden;
    options.Registration.Phone =
        HelloUiRegistrationFieldMode.Required;
});
```

`DisplayName` defaults to `Required`; `Email`, `UserName` and `Phone` default
to `Optional`. At least one of those three login identifiers must remain
visible. Password registration additionally requires the user to fill at least
one visible identifier even when every visible identifier is optional.
External registration may leave optional local identifiers empty because the
validated provider binding is itself a sign-in method, but fields configured
as `Required` are still enforced.

`Locale` defaults to `Hidden`. The selected UI language belongs to the
protected UI preference cookie and is intentionally separate from profile
data. A host that really stores a profile locale can opt in with
`HelloUiRegistrationFieldMode.Optional` or `Required`. If `DisplayName` is
hidden, the profile factory receives an empty value and must define the host's
own display-name fallback.

The ready Server exposes the same modes under
`SkopkaHello:Ui:Registration:{DisplayName|Email|UserName|Phone|Locale}`. For
example, environment variables for email-only registration are:

```text
SkopkaHello__Ui__Registration__Email=Required
SkopkaHello__Ui__Registration__UserName=Hidden
SkopkaHello__Ui__Registration__Phone=Hidden
```

These options intentionally govern the packaged Razor fields. The typed
headless `POST /auth/register` contract remains host-facing and accepts the
three optional identifiers subject to the shared Identity rule that at least
one usable login handle is present. Registration consent is different: its
policy is shared by Razor, password API and external/OIDC registration.

Select only the page groups the host needs. For example, a host-owned account
area can keep only the packaged login page:

```csharp
services.AddSkopkaHelloUi<MyProfile, MyProfileUiFactory>(options =>
{
    options.EnabledPages = HelloUiPages.Login;
    options.AuthenticatedRedirectPath = "/admin";
});
```

The default is `HelloUiPages.All`. A disabled Razor page stays in the package,
but no selector publishes it as an HTTP endpoint, so requests return 404.
Sessions, AccountSecurity and ExternalIdentity require Account, and all page
groups require Login. Login without Account requires a local absolute
`AuthenticatedRedirectPath`. A valid local `ReturnUrl` takes priority after a
successful login. `SkopkaHelloOptions.SelfRegistrationEnabled = false` still
removes registration even when its UI flag is selected.

`IHelloUiProfileFactory<TProfile>` maps the registration form profile
(`DisplayName` and optional `Locale`) to the host's JSON profile type and
returns `OperationResult<TProfile>`. Profile construction therefore stays
outside Razor page models.

### UI localization

Localization is opt-in for package consumers. The package includes complete
English and Russian catalogs. Culture selection uses a protected UI preference
cookie, then (when enabled) `Accept-Language`, then the configured default; it
does not add a culture segment to the stable Hello routes.

```csharp
services.AddSkopkaHelloUi<MyProfile, MyProfileUiFactory>(options =>
{
    options.ApplicationHomeUrl = "https://app.example.com/";
    options.TermsOfServiceUrl = "/terms";
    options.PrivacyPolicyUrl = "https://legal.example.com/privacy";
    options.Localization.Enabled = true;
    options.Localization.DefaultCulture = "ru";
    options.Localization.UseAcceptLanguageHeader = false;

    options.Localization.RemoveCulture("en");
    options.Localization.AddDictionaryFile(
        "ru",
        "Localization/skopka-hello.ru.override.json");
});
```

For a single-language Russian host, keeping localization enabled and removing
English leaves one supported culture, applies `ru` to every Hello/Admin Razor
request and suppresses the footer selector. The equivalent replacement API is:

```csharp
options.Localization.SetSupportedCultures(
    new HelloUiCulture("ru", "Русский"));
```

Call `AddDictionaryFile` after `SetSupportedCultures`, because replacement
also clears dictionary-file registrations for the previous selection.

`UseAcceptLanguageHeader` defaults to `true` for compatibility. Set it to
`false` when the configured default must be used for first-time visitors and
language changes should happen only through the packaged selector. The culture
preference cookie continues to take priority in both modes.

When `Enabled` is `false`, cookie/header selection and the culture endpoint
remain disabled, but Hello still applies `DefaultCulture` to the request and
sets `Content-Language`. Consequently the packaged layouts render the matching
`<html lang>` without changing process-wide culture defaults.

Set `ApplicationHomeUrl` to render the localized return link in the packaged
header. A local absolute path is accepted for same-host applications; a
cross-origin application must use an absolute HTTPS URL without credentials,
query or fragment.

Set `TermsOfServiceUrl` and/or `PrivacyPolicyUrl` to render localized legal
document links in the packaged footer and a separate required consent checkbox
for each configured document on both registration forms. Each configured URL
also contributes to the shared application policy, so password and external
registration fail before Identity when the matching acceptance is missing.
Headless requests provide `acceptTermsOfService` and
`acceptPrivacyPolicy`; omission is equivalent to `false`. Values use the same
safe local absolute path or absolute HTTPS URL rules as `ApplicationHomeUrl`.

Hello captures the accepted flags and one server-side `AcceptedAt` timestamp.
`HelloUiRegistrationProfile.RegistrationConsent` gives that evidence to
`IHelloUiProfileFactory<TProfile>.Create`, allowing the host to map it into the
profile written by the same Identity registration operation. For the headless
API, implement `IHelloRegistrationConsentProfileEnricher<TProfile>` on the same
factory (the UI registration helper detects and registers it). `Enrich` receives
the client-bound profile and trusted evidence immediately before Identity; it
must overwrite or clear any client-provided evidence fields. API-only hosts can
register their enricher directly and configure requirements through
`SkopkaHelloOptions.RegistrationConsent`.
When packaged registration pages are enabled, every core requirement must have
the corresponding UI document URL; startup fails instead of rendering a form
that cannot satisfy the shared policy.

The host owns document content and revision identifiers. Store the applicable
revision beside the flags and timestamp according to the host's retention and
audit policy; Hello deliberately does not infer revisions from URLs.

Dictionary paths are absolute or relative to the host content root. Files are
read once at startup, are never served as static content and can contain a
partial override. Host values win over packaged values; missing values fall
back through the parent culture, the configured default and English.

```json
{
  "culture": "de",
  "texts": {
    "Layout.SignIn": "Anmelden",
    "Account.Greeting": "Hallo, {0}"
  }
}
```

Invalid JSON, a mismatched culture or duplicate keys fail startup. The language
selector is rendered in the footer when localization is enabled and more than
one culture is configured. It posts to `{UiPathPrefix}/culture` with
antiforgery validation and accepts only a local return URL. Profile locale,
host-provided profile labels and provider display names remain host-owned data;
they are not inferred from the UI preference cookie.

To expose host-defined profile fields on the account page, the same factory
can also implement `IHelloUiProfileEditor<TProfile>`:

```csharp
public sealed class MyProfileUiFactory
    : IHelloUiProfileFactory<MyProfile>,
      IHelloUiProfileEditor<MyProfile>
{
    // Create(...) and GetDisplayName(...) omitted.

    public IReadOnlyList<HelloUiProfileField> GetFields(MyProfile profile) =>
    [
        new("displayName", "Display name", profile.DisplayName,
            AutoComplete: "name", Required: true, MaximumLength: 200),
        new("locale", "Locale", profile.Locale, MaximumLength: 32),
    ];

    public OperationResult<MyProfile> Update(
        MyProfile current,
        IReadOnlyDictionary<string, string?> values)
    {
        values.TryGetValue("displayName", out var displayName);
        values.TryGetValue("locale", out var locale);
        return string.IsNullOrWhiteSpace(displayName)
            ? OperationResultFactory.Fail<MyProfile>(
                new Error(
                    "profile.validation",
                    "Profile validation failed.",
                    ErrorType.Validation,
                    new ValidationDetails(
                        new Dictionary<string, string[]>
                        {
                            ["displayName"] =
                            ["Display name is required."],
                        })))
            : OperationResultFactory.Success(
                new MyProfile(displayName.Trim(), locale?.Trim()));
    }
}
```

Hello renders the declared fields, but the host owns their schema, validation
and mapping. The update is sent to Skopka.Identity with the user's current
optimistic version. If the factory does not implement the editor, generic
profile editing is simply omitted from the built-in UI; the typed
`PUT /account/profile` API remains available.

`ValidationDetails` keys are the names passed to `HelloUiProfileField`; Hello
maps them to the dynamic `ProfileValues[<name>]` inputs. A detail whose key does
not match a rendered field falls back to the page validation summary, so a
message is never silently discarded. Flat errors without details also appear
in that summary.

### Host pages and the current UI user

Host-owned Razor pages can read the online-validated Hello cookie without
depending on private claim names. `AddSkopkaHelloUi<TProfile, TProfileFactory>`
registers `IHelloUiUserAccessor`; it returns `null` when the UI ticket is absent
or no longer valid:

```csharp
public sealed class HeaderModel(IHelloUiUserAccessor users)
{
    public Task<HelloUiUser?> GetUserAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => users.GetAsync(httpContext, cancellationToken);
}
```

`HelloUiUser` exposes only `UserId`, `SessionId` and `DisplayName`. The accessor
authenticates with `HelloUiDefaults.AuthenticationScheme`, whose cookie events
validate the logical session online.

For host role-gated pages, register a policy backed by the current Identity
membership rather than a role claim in the cookie:

```csharp
services.AddSkopkaHelloCurrentRolePolicy<MyProfile>(
    "Host.Billing",
    "Billing",
    HelloUiDefaults.AuthenticationScheme);

services.AddRazorPages(options =>
    options.Conventions.AuthorizePage("/Billing", "Host.Billing"));
```

Pass the UI authentication scheme for browser pages because a Hello host may
use bearer authentication as its default scheme. The optional scheme argument
can be omitted when the surrounding policy composition already selects the
correct authenticated principal. Every authorization check queries the current
role and membership through `IIdentityRoleService<TProfile>`; the UI ticket
does not gain stale role claims.

The built-in pages are derived from `SkopkaHelloOptions.UiPathPrefix`. With the
default `/hello` prefix they are:

```text
/hello/register
/hello/login
/hello/forgot-password
/hello/reset-password
/hello/resend-confirmation
/hello/confirm-email
/hello/resend-phone-confirmation
/hello/confirm-phone
/hello/external/complete
/hello/external/register
/hello/account
/hello/account/sessions
/hello/account/change-password
/hello/account/security
/hello/account/external-logins
/hello/culture
```

The custom stylesheet contract is:

```text
GET /_content/Skopka.Hello.UI/custom.css
```

The ready server reads only the file configured by
`SkopkaHello:Customization:CssFilePath`. It does not expose the containing
directory. The response uses `text/css`, `X-Content-Type-Options: nosniff` and
`Cache-Control: no-cache`, so replacing the mounted file does not require an
application restart.

Mount an operator-owned host directory read-only when running the published
container:

```shell
docker run \
  --mount type=bind,source=/host/my-theme,target=/var/lib/skopka-hello/customization,readonly \
  -e SkopkaHello__Customization__CssFilePath=/var/lib/skopka-hello/customization/custom.css \
  ghcr.io/skopka/hello:<version>
```

The custom file is linked after the packaged stylesheet, so its declarations
win at equal specificity. The primary theme variables are:

```css
:root {
  --skopka-hello-color-background: #f5f6fb;
  --skopka-hello-color-surface: #ffffff;
  --skopka-hello-color-text: #202235;
  --skopka-hello-color-muted: #676b7d;
  --skopka-hello-color-primary: #6658d3;
  --skopka-hello-color-primary-hover: #5548bd;
  --skopka-hello-color-danger: #b42318;
  --skopka-hello-color-border: #dcdfea;
  --skopka-hello-color-focus: #8f84eb;
  --skopka-hello-font-family: system-ui, sans-serif;
  --skopka-hello-radius: 0.8rem;
  --skopka-hello-shadow: 0 1rem 3rem rgb(39 42 68 / 10%);
}
```

Set `BuiltInStylesEnabled = false` in `AddSkopkaHelloUi` to retain the
page markup and custom stylesheet without loading the packaged CSS. The ready
server also accepts
`SkopkaHello__Customization__BuiltInStylesEnabled=false`.

The public custom CSS request URL can be changed with
`SkopkaHello:Customization:CssRequestPath`. It must be an absolute path without
a query string, fragment, escaping or route-template syntax. Startup fails when
the path collides with a configured Hello UI route, a reserved API namespace or
an endpoint already mapped by the host. Map the Hello UI after host GET routes
so those collisions can also be detected.

External-provider UI uses the same color, radius, typography and button
variables. Stable selectors and customization hooks include:

```text
.hello-external-providers
.hello-provider-button
.hello-divider
.hello-provider-list
.hello-provider
.hello-provider-actions
.hello-step-up
.hello-status-error
[data-hello-provider="google"]
[data-hello-linked-providers]
[data-hello-available-providers]
[data-hello-step-up]
```

The provider id in `data-hello-provider` is the normalized configured id, so a
mounted stylesheet can add provider-specific styling without changing Razor
markup. The read-only CSS volume and hot replacement behavior are unchanged.
Avoid remote provider logo URLs when their tracking or referrer behavior is not
acceptable; package assets with the host or use operator-controlled CSS.

## OAuth/OIDC and external providers

External provider support lives in `Skopka.Hello.Oidc` and uses the maintained
ASP.NET Core OpenID Connect handler. The login page renders enabled providers
from `IHelloOidcProviderCatalog`; display names are Razor-encoded and provider
tokens or subjects are never rendered. The account page uses the same catalog
for link choices and shows only safe linked-provider labels and timestamps.

Provider branding is presentation only. The stable configured provider id and
validated exact `sub` form the Identity key; a CSS class, display name or
matching email cannot affect account linking. External registration and link
or unlink continue through shared `OperationResult` application operations.

This module is an external OIDC client adapter. The separate optional
`Skopka.Hello.AuthorizationServer` package issues tokens to first-party clients;
the theming surface cannot add clients, change redirect URIs or affect protocol
decisions.
