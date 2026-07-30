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
});
```

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

The built-in pages are:

```text
/hello/register
/hello/login
/hello/account
/hello/account/sessions
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

The compose stack mounts `deploy/customization` read-only:

```yaml
volumes:
  - ./customization:/var/lib/skopka-hello/customization:ro
```

Edit `deploy/customization/custom.css`, or mount another host directory:

```shell
docker run \
  --mount type=bind,source=/host/my-theme,target=/var/lib/skopka-hello/customization,readonly \
  -e SkopkaHello__Customization__CssFilePath=/var/lib/skopka-hello/customization/custom.css \
  skopka-hello:local
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
a query string or fragment.

## OAuth/OIDC and external providers

Protocol support is deferred. It will live in `Skopka.Hello.Oidc` and use a
maintained protocol implementation. Provider/subject pairs may reach
`IExternalLoginService<TProfile>` only after callback state, nonce, PKCE and
token validation. Matching email alone must never auto-link accounts.
