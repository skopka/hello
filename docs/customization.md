# Customization

## Profile type

Define one JSON-serializable profile type for the host:

```csharp
public sealed record MyProfile(string DisplayName, string? Locale);
```

`RegisterRequest<TProfile>` binds its `profile` property directly to this type.
Profile schema changes are application data migrations. Do not place ASP.NET,
EF, credential or protocol types in a profile.

## Hello options

```csharp
services.AddSkopkaHello<MyProfile>(options =>
{
    options.ClientName = "my-web-client";
    options.CookieSameSite = SameSiteMode.Strict;
    options.SelfRegistrationEnabled = false;
    options.UiPathPrefix = "/accounts";
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

The sink returns `OperationResult`, must return quickly and must not throw. It is
observability, not durable audit. Use `IHelloAuditOutbox` and
`HelloAuditOutboxRecord` inside an application-owned transaction when durability
and atomicity are required.

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
});

app.MapStaticAssets();
app.MapSkopkaHelloUi();
```

`IHelloUiProfileFactory<TProfile>` maps the registration form profile
(`DisplayName` and optional `Locale`) to the host's JSON profile type and
returns `OperationResult<TProfile>`. Profile construction therefore stays
outside Razor page models.

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
                    "profile.display_name_required",
                    "Display name is required.",
                    ErrorType.Validation))
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

The built-in pages are derived from `SkopkaHelloOptions.UiPathPrefix`. With the
default `/hello` prefix they are:

```text
/hello/register
/hello/login
/hello/forgot-password
/hello/reset-password
/hello/resend-confirmation
/hello/confirm-email
/hello/external/complete
/hello/external/register
/hello/account
/hello/account/sessions
/hello/account/change-password
/hello/account/security
/hello/account/external-logins
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

This module is an external OIDC client adapter. It does not implement an
OAuth/OIDC authorization server, and the project does not promise one through
the theming surface.
