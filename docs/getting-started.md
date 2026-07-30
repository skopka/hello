# Getting Started

## Prerequisites

- .NET SDK 10.0.101 or a compatible patch;
- PostgreSQL;
- Docker Engine for integration tests and the provided compose stack;
- Skopka.Identity `0.1.0` packages.

For adjacent source repositories, run
`scripts/sync-local-identity-packages.ps1`. The copied `.nupkg` files are ignored
by Git and feed restore plus the Docker build.

## Configure the server

Required configuration:

```text
ConnectionStrings__Identity=Host=localhost;Port=5432;Database=skopka_hello;Username=skopka;Password=...
SkopkaHello__Jwt__SigningKey=<Base64 encoded 32+ random bytes>
```

Generate a development signing key in PowerShell:

```powershell
[Convert]::ToBase64String(
  [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Optional settings:

```text
SkopkaHello__Jwt__Issuer=https://localhost:8080
SkopkaHello__Jwt__Audience=skopka-hello-api
SkopkaHello__Jwt__ValidateSessionOnEveryRequest=false
SkopkaHello__Database__ApplyMigrations=false
SkopkaHello__DataProtection__KeyPath=/protected/data-protection
```

`ApplyMigrations=true` is intended for local development or a single controlled
deployment job, not every production replica.

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

Open the browser UI at:

```text
https://localhost:8443/hello
```

The plain HTTP launch profile explicitly disables secure cookies for local
testing and can be opened at `http://localhost:8080/hello`. Never copy this
override into production.

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

Every failure produced from an operation result uses
`application/problem+json`, including `code` and `traceId` extensions.

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
