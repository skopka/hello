# Getting Started

## Prerequisites

- .NET SDK 10.0.101 or a compatible patch;
- PostgreSQL;
- Docker Engine for integration tests;
- published Skopka.Identity `0.11.0` packages.

## Configure the server

The checked-in development configuration connects to an independently managed
PostgreSQL instance at `127.0.0.1:5432` and uses
`https://localhost:8443` as both its public origin and JWT issuer. It expects
database `skopka_hello`, user `skopka` and the localhost-only development
password `skopka-local`. Apply migrations from the repository root before the
first server start:

```powershell
dotnet run --project .\src\Skopka.Hello.Server -- --migrate
```

The repository does not create, start or stop the local PostgreSQL instance.
If its database, user, password, host or port differs, override
`ConnectionStrings__Identity` to match. The known `skopka-local` password is
strictly a localhost development default.

The development file also contains three distinct public test-only keys so a
fresh clone can start without additional secret setup. These values provide no
security and must never be used outside localhost development. The project
excludes `appsettings.Development.json` from publish output, and `.dockerignore`
keeps it out of the Docker build context.

Optionally replace the public examples with personal local keys in ASP.NET Core
user secrets:

```powershell
$serverProject = ".\src\Skopka.Hello.Server\Skopka.Hello.Server.csproj"
function New-DevelopmentKey {
  $keyBytes = New-Object byte[] 32
  $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
  try {
    $generator.GetBytes($keyBytes)
    [Convert]::ToBase64String($keyBytes)
  }
  finally {
    $generator.Dispose()
  }
}

dotnet user-secrets set "SkopkaHello:Jwt:Keys:v1" `
  (New-DevelopmentKey) `
  --project $serverProject
dotnet user-secrets set "SkopkaHello:RateLimiting:Keys:v1" `
  (New-DevelopmentKey) `
  --project $serverProject
dotnet user-secrets set "SkopkaHello:Verification:Keys:v1" `
  (New-DevelopmentKey) `
  --project $serverProject
```

Equivalent required configuration for another environment is:

```text
ConnectionStrings__Identity=Host=localhost;Port=5432;Database=skopka_hello;Username=skopka;Password=...;GSS Encryption Mode=Disable
SkopkaHello__Jwt__Keys__v1=<Base64 encoded 32+ random bytes>
SkopkaHello__RateLimiting__Keys__v1=<different Base64 encoded 32+ random bytes>
SkopkaHello__Verification__Keys__v1=<third Base64 encoded 32+ random bytes>
SkopkaHello__PublicOrigin=https://localhost:8443
```

Optional settings:

```text
SkopkaHello__Jwt__Issuer=https://localhost:8443
SkopkaHello__Jwt__Audience=skopka-hello-api
SkopkaHello__Jwt__ValidateSessionOnEveryRequest=false
SkopkaHello__Jwt__CurrentVersion=v1
SkopkaHello__SelfRegistration__Enabled=true
SkopkaHello__Totp__Enabled=true
SkopkaHello__Totp__Issuer=Skopka.Hello
SkopkaHello__Ui__PathPrefix=/hello
SkopkaHello__Ui__Registration__Email=Optional
SkopkaHello__Ui__Registration__UserName=Optional
SkopkaHello__Ui__Registration__Phone=Optional
SkopkaHello__Ui__Registration__Locale=Hidden
SkopkaHello__Ui__Localization__Enabled=true
SkopkaHello__Ui__Localization__DefaultCulture=en
SkopkaHello__Admin__ApiPathPrefix=/admin
SkopkaHello__Admin__RazorUiEnabled=true
SkopkaHello__Admin__RevokeSessionsOnRoleRemoval=Always
SkopkaHello__Persistence__DurableDeliveryEnabled=true
SkopkaHello__Persistence__AuditEnabled=true
SkopkaHello__Persistence__AuditRetention=90.00:00:00
SkopkaHello__RateLimiting__CurrentVersion=v1
SkopkaHello__Verification__CurrentVersion=v1
SkopkaHello__DataProtection__KeyPath=/protected/data-protection
SkopkaHello__DataProtection__CertificatePath=/run/secrets/data-protection.pfx
SkopkaHello__DataProtection__CertificatePassword=<secret>
```

`PublicOrigin` is the externally reachable HTTP(S) origin used to build
confirmation and password-reset links and external OIDC callback URIs. It must
not contain credentials, a path, query or fragment. It is configuration, never
inferred from the request `Host` header. `Jwt:Issuer` defaults to this trusted
origin; set it explicitly only when tokens deliberately use a different
issuer.

`SelfRegistration:Enabled` controls both password and external OIDC account
creation. When false, the ready Server does not map `/auth/register`, the
password registration page or the pending external-registration page. Existing
users can still sign in and manage linked providers.

Enabled registration is protected by persistent client and global limits under
`SelfRegistration:ClientPermitLimit`, `ClientWindow`, `GlobalPermitLimit` and
`GlobalWindow`. Hosts can add CAPTCHA, invitation or tenant admission rules by
registering one or more `IHelloRegistrationAdmissionPolicy` implementations;
these policies run before a rate-limit permit is consumed.

`Ui:PathPrefix` is a startup-only route prefix for the built-in Razor UI. It
defaults to `/hello` and must be a non-empty absolute prefix other than `/`.
It is not ASP.NET Core `PathBase`: API, health, static assets and the raw OIDC
callback remain at their existing root-relative paths. Changing either setting
requires an application restart. Prefixes inside the reserved `/auth`,
`/account`, `/health`, `/swagger`, `/openapi`, `/_content` and
`/signin-skopka-oidc` namespaces are rejected at startup to prevent ambiguous
routes.

Library hosts can set `SkopkaHelloUiOptions.LayoutPath` to a local absolute
compiled Razor layout path when Hello and Admin pages should use the host's
document shell. Null keeps the packaged layouts. The host layout must render
the body, keep unsupported sections optional and use root-absolute resource
URLs; see [customization](customization.md#ui-and-styles).

`Ui:Localization:Enabled` enables the packaged English/Russian language
selector. `Ui:Localization:DefaultCulture` supplies the fallback. Additional
cultures and server-side JSON dictionaries can be configured through
`Ui:Localization:Cultures`; see [customization](customization.md#ui-localization).
In-process hosts can remove a built-in culture with `RemoveCulture` or replace
the selection with `SetSupportedCultures`; a one-culture selection suppresses
the selector. When localization is disabled, Hello still applies the default
culture and `Content-Language` to its Razor requests. Culture selection does
not change the Razor route paths.

`Ui:Registration` configures the built-in password and external registration
forms with `Hidden`, `Optional` or `Required` modes. `DisplayName` is required
by default, `Email`/`UserName`/`Phone` are optional, and `Locale` is hidden.
At least one login identifier must remain visible. See
[customization](customization.md#registration-fields) for phone-only,
email-only and user-name-only examples and validation behavior.

The user/role administration API uses `Admin:ApiPathPrefix`; its Razor pages
are composed under `{UiPathPrefix}{AdminApiPathPrefix}/users` and `/roles`.
Configure current read/manage/delete role names and bootstrap the first
administrator as described in [administration](administration.md).

## Configure an external OIDC provider

The ready configuration contains a disabled Google example. Keep a provider
disabled until its authority, exact callback URI and credentials are ready.
Supply credentials outside source and enable them together:

```text
SkopkaHello__ExternalOidc__Providers__google__Enabled=true
SkopkaHello__ExternalOidc__Providers__google__ClientId=<client id>
SkopkaHello__ExternalOidc__Providers__google__ClientSecret=<client secret>
```

The full provider schema under
`SkopkaHello:ExternalOidc:Providers:{providerId}` is:

```text
Enabled                  false until configured
DisplayName              user-facing provider label
Authority                fixed absolute OIDC authority
ClientId                 client identifier for this server-side client
ClientSecret             secret supplied outside source
RequireHttpsMetadata     true
Order                    display order
Scopes                   optional additional scopes
```

The adapter always requests `openid`, `profile` and `email`; configured scopes
are additive. It normalizes provider ids to lower case for its configuration
keys and callback path segments, then passes that canonical value through
Identity's own external-key normalization. Once an id has been used in
production, keep its authority, tenant and client trust boundary fixed; use a
new id for a different issuer.
For the checked-in HTTPS profile and `google` id, register:

```text
https://localhost:8443/signin-skopka-oidc/google
```

The HTTPS authority, client id and callback must also be allowed in the provider
console. With the default UI prefix, the internal completion page is
`/hello/external/complete`; it is not a provider callback. The Server derives
the callback only from trusted `PublicOrigin` and the configured provider id.

External and pending OIDC tickets expire after five and ten minutes by default;
`ExternalCookieLifetime` and `PendingCookieLifetime` accept values from one to
thirty minutes. Keep their default `__Host-` cookie names in HTTPS deployments.
Terminal submissions are one-use. With the configured persistent HMAC rate
limiter, the ready Server shares this replay guard across replicas; custom hosts
without it should register an atomic shared `IHelloOidcFlowStore` before
scaling out.

To select and enable the built-in queued SMTP email provider:

```text
SkopkaHello__Delivery__EmailProviderId=smtp
SkopkaHello__Delivery__SmsProviderId=
SkopkaHello__Delivery__VerificationChannel=Email
SkopkaHello__Delivery__RequireTotpWhenEnabled=false
SkopkaHello__Delivery__AnonymousRequestQueueCapacity=256
SkopkaHello__Delivery__Smtp__ProviderId=smtp
SkopkaHello__Delivery__Smtp__Host=smtp.example.com
SkopkaHello__Delivery__Smtp__Port=587
SkopkaHello__Delivery__Smtp__EnableSsl=true
SkopkaHello__Delivery__Smtp__UserName=...
SkopkaHello__Delivery__Smtp__Password=...
SkopkaHello__Delivery__Smtp__FromAddress=accounts@example.com
SkopkaHello__Delivery__Smtp__FromName=Example Accounts
SkopkaHello__Delivery__Smtp__QueueCapacity=256
SkopkaHello__Delivery__Smtp__Localization__DefaultCulture=ru
```

Leave `EmailProviderId` and `Host` empty to keep email delivery disabled. The
configured provider id must identify exactly one provider registered for the
same channel; missing, duplicate and channel-mismatched registrations fail at
startup. Custom hosts add an SMS implementation with
`AddSkopkaHelloSmsProvider<TProvider>()`; the ready Server does not ship a
vendor-specific SMS adapter.

`VerificationChannel` selects where password-change and external-account
mutation codes are sent. Set it to `Email` or `Sms`; the selected address must
be confirmed and the matching provider must be configured. A challenge never
falls back to the other channel after it has been issued.

`Totp:Enabled` exposes authenticator enrollment in the API and account-security
page. The ready Server registers encrypted RFC 6238 storage automatically.
Set `Delivery:RequireTotpWhenEnabled=true` to use an enabled authenticator
instead of the confirmed contact for every built-in sensitive action,
including admin actions. Users without an enabled factor continue through the
configured contact channel.

The SMTP provider packages complete English and Russian dictionaries. A custom
host can select and partially override them without replacing the queued or
durable provider:

```csharp
services.AddSkopkaHelloSmtpProvider(options =>
{
    // SMTP connection settings omitted.
    options.Localization.DefaultCulture = "ru";
    options.Localization.AddDictionaryFile(
        "ru",
        "Localization/account-email.ru.override.json");
});
```

For example, the override can change only the account-security wording while
the packaged Russian catalog supplies every other value:

```json
{
  "culture": "ru",
  "texts": {
    "Email.AccountSecurityVerification.AccountDelete.Subject": "Подтверждение удаления аккаунта",
    "Email.AccountSecurityVerification.AccountDelete.Introduction": "Удаление аккаунта необратимо: вместе с ним исчезнут зачисления и статистика."
  }
}
```

Dictionary files use the UI dictionary shape (`culture` plus `texts`). Stable
keys are published in `HelloAccountEmailTextKeys`; every value of
`HelloAccountMessageKind`, including `AdminActionVerification`, has a subject
and introduction key. The host-level default culture is used for every email.

`IHelloAccountMessageSender` remains the application-facing port and dispatches
semantic messages to the selected provider. The reusable SMTP adapter defaults
to a bounded in-memory queue. The ready Server instead persists anonymous
requests and configured email messages in PostgreSQL, then calls SMTP directly
from the durable outbox worker. Anonymous password-reset and
contact-confirmation requests enter the inbox before lookup or token issuance.
The in-memory fallback capacity is `AnonymousRequestQueueCapacity`. The ready
Server rate-limits inbox admission
by trusted client key and normalized target using the Identity verification
limits and resend cooldown. Denied or capacity-exhausted anonymous requests are
silently dropped after validation and still receive `202 Accepted`.
Email confirmation, password reset, password-change OTP and purpose-specific
external link/unlink OTP delivery all use this dispatcher. Step-up challenge
requests report a delivery error when their channel has no configured provider.

The web process never changes the database schema. Run the one-shot
`--migrate` command before starting a new application version. It reads only
`ConnectionStrings:Identity`, applies both Identity migrations and the
versioned `skopka_hello` persistence schema, exits successfully when both are
current, and must be serialized by the deployment platform.

Durable delivery is at-least-once. Rows are leased for one minute, retried up
to eight times and retained for seven days after terminal failure by default.
The normalized target and complete provider message are Data
Protection-encrypted before persistence. Keep the shared key ring and its old
keys available while queued or retained records may still reference them.
`AuditEnabled=true` stores enriched Identity events after their domain
transaction commits; an audit write failure cannot roll that transaction back.

Rate-limit and verification key versions are non-secret stable identifiers.
Generate each purpose's key independently; never reuse the JWT signing key,
rate-limit key or verification key for another purpose.

## Run

```powershell
dotnet restore .\Skopka.Hello.slnx --configfile .\NuGet.Config
dotnet dev-certs https --trust
dotnet run --project .\src\Skopka.Hello.Server `
  --launch-profile https
```

The `https` profile listens on `https://localhost:8443` and also exposes
`http://localhost:8080` for diagnostics. Authentication routes that issue
secure cookies must be tested through the HTTPS address. By default it uses the
public test-only keys from `appsettings.Development.json`; user secrets override
them when configured. The Development file is excluded from publish and Docker
output.

External OIDC also requires this HTTPS profile. Its correlation and nonce
cookies remain `Secure` even in development. Put the OIDC client secret in user
secrets or an environment-specific secret provider; do not add it to
`appsettings.json` or `launchSettings.json`.

Open the browser UI at:

```text
https://localhost:8443/hello
```

In Development, the generated OpenAPI document and Swagger UI are available at:

```text
https://localhost:8443/openapi/v1.json
https://localhost:8443/swagger
```

Neither endpoint is mapped outside the Development environment.

The plain HTTP launch profile explicitly disables secure cookies for local
testing and can be opened at `http://localhost:8080/hello`. It explicitly keeps
the example OIDC provider disabled. Never copy this override into production or
weaken OIDC correlation cookies to make an external provider work over HTTP.

## Register and authenticate

Registration:

```http
POST /auth/register
Content-Type: application/json

{
  "userName": "alice",
  "email": "alice@example.test",
  "phone": null,
  "profile": {
    "displayName": "Alice",
    "locale": "en"
  },
  "password": "a sufficiently long passphrase",
  "acceptTermsOfService": true,
  "acceptPrivacyPolicy": true
}
```

The two acceptance fields are required only when the corresponding registration
consent policy is enabled. Configuring the packaged UI's legal-document URL
enables that policy for both Razor and headless registration. API-only hosts can
set `SkopkaHelloOptions.RegistrationConsent` directly.

Login:

```http
POST /auth/login
Content-Type: application/json

{
  "login": "alice@example.test",
  "password": "a sufficiently long passphrase"
}
```

Hello resolves the value as an email, phone number or user name in one Identity
lookup. The HTTP contract intentionally has no caller-selected handle type.

The response contains `sessionId`, `accessToken`, `accessTokenExpiresAt` and
`refreshTokenExpiresAt`. It never contains the refresh token. Preserve the
response cookies.

For refresh or cookie logout, read the
`__Host-Skopka.Hello.XSRF-TOKEN` cookie and send it:

```http
POST /auth/refresh
Cookie: __Host-Skopka.Hello.Refresh=...; __Host-Skopka.Hello.Antiforgery=...
X-CSRF-TOKEN: ...
```

Use the access token for account calls:

```http
GET /account/me
Authorization: Bearer <access-token>
```

The response includes the current optimistic `version`. Use it for account
self-service mutations so concurrent edits are detected:

```http
PUT /account/profile
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "expectedVersion": 3,
  "profile": {
    "displayName": "Alice Updated",
    "locale": "en"
  }
}
```

The same contract is available at `PUT /account/user-name`,
`PUT /account/email` and `PUT /account/phone`. Changing an email or phone
resets its confirmation. A stale version returns `409 Conflict`; callers must
reload `/account/me` instead of overwriting a concurrent change. The generic
profile type is the exact `TProfile` registered by the host. A password account
cannot remove its final user name, email or phone because that would leave the
credential without a usable login identifier. An external-only account may
remain without a local handle.

When OIDC is registered, clients may discover the safe provider catalog:

```http
GET /auth/external/providers
```

Bearer clients may list linked provider labels, timestamps, enabled state and
the current `canUnlink` decision with `GET /account/external-logins`. Provider
subjects and protocol tokens are not returned. External sign-in and
registration support both the built-in Razor UI
and a same-origin browser/SPA API. Link and unlink use the same two surfaces and
the same Identity-owned step-up. There is no native-app provider-token
callback. Native/BFF clients instead use the separate first-party authorization
server described in [authorization server](authorization-server.md).

### Same-origin browser/SPA flow

Navigate the browser, rather than calling `fetch`, to a configured provider:

```http
GET /auth/external/integration/challenge?returnUrl=%2Fapp%2Fauth-callback
```

The `returnUrl` is the local application landing path after the provider
callback. It must be an absolute local path and cannot target Hello's external
API or provider callback paths. The ASP.NET Core handler owns state, nonce,
PKCE, code redemption and token validation, then redirects the browser to that
landing path. Provider tokens and subjects are never placed in the URL or an
API response.

The challenge response also issues the normal antiforgery cookie pair. After
the browser returns, read the non-HttpOnly
`SkopkaHelloOptions.AntiforgeryRequestCookieName` cookie and submit it through
the configured `AntiforgeryHeaderName` (defaults:
`__Host-Skopka.Hello.XSRF-TOKEN` and `X-CSRF-TOKEN`):

```http
POST /auth/external/complete
X-CSRF-TOKEN: <request-token-cookie-value>
```

For an existing external login, the response has outcome `SignedIn`, contains
the normal `SessionResponse`, and writes the refresh-cookie transport. For an
unknown identity it has outcome `RegistrationRequired` and contains only the
configured provider label plus bounded display-name, verified-email and locale
hints. Fetch those hints again with `GET /auth/external/registration` or finish
the atomic registration with the host's exact `TProfile` shape:

```http
POST /auth/external/registration
Content-Type: application/json
X-CSRF-TOKEN: <request-token-cookie-value>

{
  "userName": "alice",
  "email": "alice@example.test",
  "phone": null,
  "profile": {
    "displayName": "Alice",
    "locale": "en"
  }
}
```

Each terminal POST consumes its protected flow id once. Cancel an abandoned
flow with the antiforgery-protected `DELETE /auth/external/flow`. These routes
are intended for a browser on the same origin as Hello; a native application
requires a separate public-client/BFF design.

The repository's
[`Skopka.Hello.Sample`](../samples/Skopka.Hello.Sample/README.md) includes a
framework-free reference SPA at `/app`. It keeps the access token only in the
main tab's memory. OIDC navigation opens with `noopener`; the callback sends a
completion signal over a randomly named same-origin `BroadcastChannel`, so the
provider never receives a bearer token and no token is written to browser
storage. The sample also demonstrates external registration and the full
OTP-protected link/unlink sequence.

### Link and unlink from a browser/SPA

Antiforgery tokens are bound to the current ASP.NET Core principal. Before an
authenticated cookie-backed flow, issue a fresh pair with the current Bearer
token and then read the new request-token cookie:

```http
GET /auth/antiforgery
Authorization: Bearer <access-token>
```

To link a provider, create an authenticated browser preflight:

```http
POST /account/external-logins/integration/link
Authorization: Bearer <access-token>
Content-Type: application/json
X-CSRF-TOKEN: <request-token-cookie-value>

{ "returnUrl": "/app/external-result" }
```

The response contains a local `challengeUrl`. Navigate the browser to it; the
server consumes a short-lived HttpOnly `SameSite=Strict` link-request cookie
and its atomic flow id before starting the maintained OIDC handler. After the
provider returns, POST `/auth/external/complete` with the same Bearer and CSRF
header. Outcome `LinkVerificationRequired` contains only the safe provider
label. Request and complete the configured step-up with:

```http
POST /account/external-logins/link/challenge
Authorization: Bearer <access-token>
X-CSRF-TOKEN: <request-token-cookie-value>

PUT /account/external-logins/link
Authorization: Bearer <access-token>
Content-Type: application/json
X-CSRF-TOKEN: <request-token-cookie-value>

{ "verificationCode": "123456" }
```

Unlink does not revisit the provider. Start it at
`POST /account/external-logins/{providerId}/unlink/challenge`, then send the
code to `DELETE /account/external-logins/unlink`; both requests carry Bearer
and the CSRF header. The provider id is captured in the protected pending flow,
not accepted again during completion. Link and unlink preserve at least one
enabled sign-in method, rotate the security stamp, revoke old refresh sessions
and return a replacement `SessionResponse` to the current browser.

## Sign in with an external provider

Open `{UiPathPrefix}/login` and choose an enabled provider. ASP.NET Core performs the
authorization-code flow with PKCE, state and nonce validation. After the raw
provider callback, `{UiPathPrefix}/external/complete` requires an explicit
antiforgery-protected POST.

If the validated provider/subject is already linked, Hello issues the normal
JWT/refresh session and protected UI ticket. Otherwise
`{UiPathPrefix}/external/register` collects the local profile and atomically creates the
user plus external login. A provider-verified email may prefill the form but is
not locally confirmed. An email matching another account never links it; sign
in to that account with an existing method and link from
`{UiPathPrefix}/account/external-logins`. These paths use `/hello` by default.

Link and unlink require a confirmed local contact for the configured delivery
channel. Hello sends a one-time code through `IHelloAccountMessageSender`,
binds it to the exact provider identity and action, and refuses to remove the
final enabled sign-in method. On success,
Identity rotates the security stamp, all old refresh sessions are revoked and
the current browser receives a new session. Existing stateless bearer access
tokens remain usable only until their short expiry unless online validation is
enabled.

Every failure produced from an operation result uses
`application/problem+json`, including `code` and `traceId` extensions.

## Confirm email or phone and reset a password

Request endpoints accept an email or phone and always return `202 Accepted`
for a well-formed contact, whether or not an active account exists:

```http
POST /auth/password-reset/request
Content-Type: application/json

{ "email": "alice@example.test" }
```

The email link opens a no-store Razor page. Confirmation links require a
button-backed antiforgery POST so automated mail scanners cannot mutate the
account by following a GET. API clients can submit the link values directly to
`/auth/password-reset/confirm`, `/auth/email-confirmation/confirm` or
`/auth/phone-confirmation/confirm`.

A successful password reset rotates the security stamp. Refresh sessions can no
longer be used; stateless access tokens remain valid only until their short
expiry unless online validation is enabled.

## Change an authenticated password

Password change requires an active bearer session. By default it also requires a
confirmed contact for the configured `VerificationChannel`; an enabled
authenticator replaces that dependency when `RequireTotpWhenEnabled=true`.
First request a step-up challenge:

```http
POST /account/password/change/challenge
Authorization: Bearer <access-token>
```

The response contains only `challengeId`, `expiresAt` and `deliveryChannel`
(`email`, `sms` or `authenticator`). Contact OTP is delivered through the
provider configured for that channel and is never returned by HTTP. For the
authenticator channel, submit a current TOTP or unused recovery code with both
passwords:

```http
POST /account/password/change
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "challengeId": "00000000-0000-0000-0000-000000000000",
  "verificationCode": "123456",
  "currentPassword": "current sufficiently long passphrase",
  "newPassword": "new sufficiently long passphrase"
}
```

The action, user and resource binding are created by the server from the
online-validated access token; clients cannot select them. The proof is
single-use. A successful change rotates the security stamp, revokes all
sessions and requires a fresh login. With the default prefix, the Razor page at
`/hello/account/change-password` uses the same application operation and
antiforgery-protected POSTs.

## Set or remove a password and delete an account

An externally registered account can set a password after requesting
`POST /account/password/set/challenge`, then completing:

```http
PUT /account/password
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "challengeId": "00000000-0000-0000-0000-000000000000",
  "verificationCode": "123456",
  "newPassword": "a sufficiently long passphrase"
}
```

At least one user name, email address or phone number must already be present,
so the newly configured password has a usable login identifier.

Password removal uses `POST /account/password/remove/challenge` followed by
`DELETE /account/password` with `challengeId` and `verificationCode` in the
JSON body. Hello refuses removal unless another external sign-in method is
linked. Account deletion uses `POST /account/delete/challenge` followed by
`DELETE /account` with the same completion body.

All three actions require the configured confirmed delivery contact, bind the
single-use proof to the exact action, revoke every session on success and
require a fresh sign-in where the account still exists. The built-in UI exposes
them at `/hello/account/security` with the default prefix.

## Browser session behavior

The Razor forms use the same `IHelloIdentityApplication<TProfile>` operations
as the API. Form failures are rendered from `OperationResult` validation
details. Successful login creates:

- the normal rotating refresh-token cookie;
- an encrypted `HttpOnly` Razor authentication ticket containing the
  short-lived access token;
- antiforgery cookies for form mutations.

Protected pages validate the access token online. After access-token expiry the
UI rotates the refresh session and renews its protected ticket without exposing
either token to JavaScript.
