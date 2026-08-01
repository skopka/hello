# Getting Started

## Prerequisites

- .NET SDK 10.0.101 or a compatible patch;
- PostgreSQL;
- Docker Engine for integration tests and the provided compose stack;
- published Skopka.Identity `0.5.0` packages.

## Configure the server

Required configuration:

```text
ConnectionStrings__Identity=Host=localhost;Port=5432;Database=skopka_hello;Username=skopka;Password=...
SkopkaHello__Jwt__SigningKey=<Base64 encoded 32+ random bytes>
SkopkaHello__RateLimiting__Keys__v1=<different Base64 encoded 32+ random bytes>
SkopkaHello__Verification__Keys__v1=<third Base64 encoded 32+ random bytes>
SkopkaHello__PublicOrigin=https://localhost:8443
```

Generate each development key independently in PowerShell:

```powershell
[Convert]::ToBase64String(
  [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Optional settings:

```text
SkopkaHello__Jwt__Issuer=https://localhost:8080
SkopkaHello__Jwt__Audience=skopka-hello-api
SkopkaHello__Jwt__ValidateSessionOnEveryRequest=false
SkopkaHello__RateLimiting__CurrentVersion=v1
SkopkaHello__Verification__CurrentVersion=v1
SkopkaHello__Database__ApplyMigrations=false
SkopkaHello__DataProtection__KeyPath=/protected/data-protection
```

`PublicOrigin` is the externally reachable HTTP(S) origin used to build
confirmation and password-reset links and external OIDC callback URIs. It must
not contain credentials, a path, query or fragment. It is configuration, never
inferred from the request `Host` header.

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
console. The internal `/hello/external/complete` page is not a provider
callback. The Server derives the callback only from trusted `PublicOrigin` and
the configured provider id.

External and pending OIDC tickets expire after five and ten minutes by default;
`ExternalCookieLifetime` and `PendingCookieLifetime` accept values from one to
thirty minutes. Keep their default `__Host-` cookie names in HTTPS deployments.
Terminal submissions are one-use. With the configured persistent HMAC rate
limiter, the ready Server shares this replay guard across replicas; custom hosts
without it should register an atomic shared `IHelloOidcFlowStore` before
scaling out.

To enable the built-in background SMTP sender:

```text
SkopkaHello__Delivery__Smtp__Host=smtp.example.com
SkopkaHello__Delivery__Smtp__Port=587
SkopkaHello__Delivery__Smtp__EnableSsl=true
SkopkaHello__Delivery__Smtp__UserName=...
SkopkaHello__Delivery__Smtp__Password=...
SkopkaHello__Delivery__Smtp__FromAddress=accounts@example.com
SkopkaHello__Delivery__Smtp__FromName=Example Accounts
SkopkaHello__Delivery__Smtp__QueueCapacity=256
```

Omit `Host` to leave delivery disabled, or register a custom
`IHelloAccountMessageSender` before `AddSkopkaHello<TProfile>()`. The built-in
queue is bounded and in-memory; use a durable application queue when account
messages must survive a process restart. Email confirmation, password reset,
password-change OTP and external link/unlink OTP delivery all use this adapter;
step-up challenge requests report a delivery error when no sender is configured.

`ApplyMigrations=true` is intended for local development or a single controlled
deployment job, not every production replica.

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
secure cookies must be tested through the HTTPS address. The signing key and
database connection are still supplied through environment variables and are
not stored in `launchSettings.json`.

External OIDC also requires this HTTPS profile. Its correlation and nonce
cookies remain `Secure` even in development. Put the OIDC client secret in user
secrets or an environment-specific secret provider; do not add it to
`appsettings.json` or `launchSettings.json`.

Open the browser UI at:

```text
https://localhost:8443/hello
```

The plain HTTP launch profile explicitly disables secure cookies for local
testing and can be opened at `http://localhost:8080/hello`. It explicitly keeps
the example OIDC provider disabled. Never copy this override into production or
weaken OIDC correlation cookies to make an external provider work over HTTP.

Or create `.env` from `.env.example` and run:

```powershell
docker compose -f .\deploy\docker-compose.yml up --build
```

The local compose file explicitly uses non-secure, non-`__Host-` cookie names
because it publishes plain HTTP on localhost. Keep the production default
(`SkopkaHello:Cookies:Secure=true`) behind TLS.

Compose also mounts [deploy/customization/custom.css](../deploy/customization/custom.css)
read-only. The server publishes it at
`/_content/Skopka.Hello.UI/custom.css`; changes to the mounted file are visible
without rebuilding the image.

The compose UI is available at `http://localhost:8080/hello`.
The checked-in compose stack is intended for password-flow development;
external providers remain disabled because the published endpoint is plain
HTTP. Put the container behind a trusted TLS proxy and configure the HTTPS
`PublicOrigin` before enabling OIDC.

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
  "password": "a sufficiently long passphrase"
}
```

Login:

```http
POST /auth/login
Content-Type: application/json

{
  "handle": "email",
  "login": "alice@example.test",
  "password": "a sufficiently long passphrase"
}
```

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

When OIDC is registered, clients may discover the safe provider catalog:

```http
GET /auth/external/providers
```

Bearer clients may list linked provider labels and timestamps with
`GET /account/external-logins`. Provider subjects and protocol tokens are not
returned. External sign-in, registration, link and unlink are currently browser
flows; there is no native-app token-in-query callback or external mutation API.

## Sign in with an external provider

Open `/hello/login` and choose an enabled provider. ASP.NET Core performs the
authorization-code flow with PKCE, state and nonce validation. After the raw
provider callback, `/hello/external/complete` requires an explicit
antiforgery-protected POST.

If the validated provider/subject is already linked, Hello issues the normal
JWT/refresh session and protected UI ticket. Otherwise
`/hello/external/register` collects the local profile and atomically creates the
user plus external login. A provider-verified email may prefill the form but is
not locally confirmed. An email matching another account never links it; sign
in to that account with an existing method and link from
`/hello/account/external-logins`.

Link and unlink require a confirmed local email. The UI sends a one-time code
through `IHelloAccountMessageSender`, binds it to the exact provider identity
and action, and refuses to remove the final enabled sign-in method. On success,
Identity rotates the security stamp, all old refresh sessions are revoked and
the current browser receives a new session. Existing stateless bearer access
tokens remain usable only until their short expiry unless online validation is
enabled.

Every failure produced from an operation result uses
`application/problem+json`, including `code` and `traceId` extensions.

## Confirm email and reset a password

Request endpoints accept an email and always return `202 Accepted` for a
well-formed address, whether or not an active account exists:

```http
POST /auth/password-reset/request
Content-Type: application/json

{ "email": "alice@example.test" }
```

The email link opens a no-store Razor page. Confirmation links require a
button-backed antiforgery POST so automated mail scanners cannot mutate the
account by following a GET. API clients can submit the link values directly to
`/auth/password-reset/confirm` or `/auth/email-confirmation/confirm`.

A successful password reset rotates the security stamp. Refresh sessions can no
longer be used; stateless access tokens remain valid only until their short
expiry unless online validation is enabled.

## Change an authenticated password

Password change requires an active bearer session and a confirmed email
address. First request an OTP challenge:

```http
POST /account/password/change/challenge
Authorization: Bearer <access-token>
```

The response contains only `challengeId` and `expiresAt`. The OTP is delivered
through `IHelloAccountMessageSender` and is never returned by HTTP. Submit it
with both passwords:

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
sessions and requires a fresh login. The Razor page at
`/hello/account/change-password` uses the same application operation and
antiforgery-protected POSTs.

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
