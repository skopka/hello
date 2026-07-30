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
dotnet run --project .\src\Skopka.Hello.Server
```

Or create `.env` from `.env.example` and run:

```powershell
docker compose -f .\deploy\docker-compose.yml up --build
```

The local compose file explicitly uses non-secure, non-`__Host-` cookie names
because it publishes plain HTTP on localhost. Keep the production default
(`SkopkaHello:Cookies:Secure=true`) behind TLS.

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
