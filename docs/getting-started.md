# Getting Started

## Prerequisites

- .NET SDK 10.0.101 or a compatible patch;
- PostgreSQL;
- Docker Engine for integration tests and the provided compose stack;
- published Skopka.Identity `0.7.0` packages.

## Configure the server

The checked-in development configuration connects to the local PostgreSQL
container at `127.0.0.1:5432`, applies migrations on startup and uses
`https://localhost:8443` as both its public origin and JWT issuer. Start only
the database from the repository root:

```powershell
docker compose -f .\deploy\docker-compose.postgres.yml up -d --wait
```

The database port is bound only to `127.0.0.1`. The known `skopka-local`
password is strictly a localhost development default. If `POSTGRES_DB`,
`POSTGRES_USER`, `POSTGRES_PASSWORD` or `POSTGRES_PORT` is overridden, update
`ConnectionStrings__Identity` to match. PostgreSQL initialization settings are
applied only when its named volume is first created; changing them later does
not rewrite the existing database or role password.

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

dotnet user-secrets set "SkopkaHello:Jwt:SigningKey" `
  (New-DevelopmentKey) `
  --project $serverProject
dotnet user-secrets set "SkopkaHello:RateLimiting:Keys:v1" `
  (New-DevelopmentKey) `
  --project $serverProject
dotnet user-secrets set "SkopkaHello:Verification:Keys:v1" `
  (New-DevelopmentKey) `
  --project $serverProject
```

Stop the database without deleting its named volume:

```powershell
docker compose -f .\deploy\docker-compose.postgres.yml down
```

Equivalent required configuration for another environment is:

```text
ConnectionStrings__Identity=Host=localhost;Port=5432;Database=skopka_hello;Username=skopka;Password=...
SkopkaHello__Jwt__SigningKey=<Base64 encoded 32+ random bytes>
SkopkaHello__RateLimiting__Keys__v1=<different Base64 encoded 32+ random bytes>
SkopkaHello__Verification__Keys__v1=<third Base64 encoded 32+ random bytes>
SkopkaHello__PublicOrigin=https://localhost:8443
```

Optional settings:

```text
SkopkaHello__Jwt__Issuer=https://localhost:8443
SkopkaHello__Jwt__Audience=skopka-hello-api
SkopkaHello__Jwt__ValidateSessionOnEveryRequest=false
SkopkaHello__SelfRegistration__Enabled=true
SkopkaHello__Ui__PathPrefix=/hello
SkopkaHello__RateLimiting__CurrentVersion=v1
SkopkaHello__Verification__CurrentVersion=v1
SkopkaHello__Database__ApplyMigrations=false
SkopkaHello__DataProtection__KeyPath=/protected/data-protection
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

`IHelloAccountMessageSender` remains the application-facing port and dispatches
semantic messages to the selected provider. The SMTP provider acknowledges
successful enqueue, not remote delivery. Its queue is bounded and in-memory,
and its worker skips messages that expire while waiting. Replace the sender
with a durable application queue when messages must survive a process restart.
Anonymous password-reset and contact-confirmation requests first enter a
separate bounded queue before lookup or token issuance; its capacity is
`AnonymousRequestQueueCapacity`. The ready Server rate-limits queue admission
by trusted client key and normalized target using the Identity verification
limits and resend cooldown. Denied or capacity-exhausted anonymous requests are
silently dropped after validation and still receive `202 Accepted`.
Email confirmation, password reset, password-change OTP and purpose-specific
external link/unlink OTP delivery all use this dispatcher. Step-up challenge
requests report a delivery error when their channel has no configured provider.

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

Bearer clients may list linked provider labels and timestamps with
`GET /account/external-logins`. Provider subjects and protocol tokens are not
returned. External sign-in, registration, link and unlink are currently browser
flows; there is no native-app token-in-query callback or external mutation API.

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

Link and unlink require a confirmed local email. The UI sends a one-time code
through `IHelloAccountMessageSender`, binds it to the exact provider identity
and action, and refuses to remove the final enabled sign-in method. On success,
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

Password change requires an active bearer session and a confirmed contact for
the configured `VerificationChannel`. First request an OTP challenge:

```http
POST /account/password/change/challenge
Authorization: Bearer <access-token>
```

The response contains only `challengeId`, `expiresAt` and `deliveryChannel`
(`email` or `sms`). The OTP is delivered through the provider configured for
that channel and is never returned by HTTP. Submit it with both passwords:

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
