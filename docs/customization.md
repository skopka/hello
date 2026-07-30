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

The first vertical slice has no decorative UI. Future built-in Razor UI belongs
in `Skopka.Hello.UI`; it must call the same application services as the API.
CSS custom properties will be the theming contract, and hosts will be able to
disable built-in styles.

## OAuth/OIDC and external providers

Protocol support is deferred. It will live in `Skopka.Hello.Oidc` and use a
maintained protocol implementation. Provider/subject pairs may reach
`IExternalLoginService<TProfile>` only after callback state, nonce, PKCE and
token validation. Matching email alone must never auto-link accounts.
