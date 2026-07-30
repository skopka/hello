# Agent Instructions

This is the primary context for agents working on Skopka.Hello. Read it before a
module-local `AGENTS.md`.

## Product boundary

Skopka.Hello is the ASP.NET Core transport/composition layer for Skopka.Identity.
Do not recreate users, credentials, external logins, roles, verification,
security stamps or refresh-session persistence here. Do not change the
Skopka.Identity architecture from this repository.

Expected identity failures use
`Skopka.Abstraction.OperationResult.OperationResult`; map them to the common
ProblemDetails or Razor validation contract and do not turn them into HTTP 500
responses.

## Current vertical

The implemented surfaces are the register/login/session/account Minimal API and
the Razor registration, login, account and active-session UI. Registration must
remain atomic through `IIdentityRegistrationService<TProfile>`. HTTP handlers
call `IHelloIdentityApplication<TProfile>` or Skopka.Identity application
services, never EF stores.

Refresh tokens stay only in `Secure`, `HttpOnly` cookies. Cookie-authorized
mutations require antiforgery validation. API access tokens are returned as
JSON/bearer tokens. The protected Razor UI ticket carries its access token in
an encrypted `HttpOnly` cookie and validates it online. Client keys come from
trusted server request context, not request DTOs.

## Modules

- `src/Skopka.Hello` - facade, shared identity application operations, cookie
  transport, request context and event/outbox contracts.
- `src/Skopka.Hello.Endpoints` - Minimal API, DTOs and ProblemDetails.
- `src/Skopka.Hello.UI` - Razor registration/login/account/session pages and
  theming, with no identity business logic.
- `src/Skopka.Hello.Oidc` - future maintained OAuth/OIDC adapter, never a
  home-grown protocol.
- `src/Skopka.Hello.Admin` - future authorized admin workflows.
- `src/Skopka.Hello.Server` - executable composition and Docker image.

Read the local `AGENTS.md` before editing any module.

## Engineering rules

- Target .NET 10 and use Central Package Management.
- Keep nullable enabled and do not suppress warnings without a documented
  reason.
- All async APIs accept `CancellationToken`.
- Never expose EF entities, security stamps, passwords or refresh tokens.
- Do not log credentials, JWTs, refresh/action/provider tokens or OTPs.
- Preserve Apache-2.0 package metadata.

## Verification

Run:

```powershell
dotnet restore .\Skopka.Hello.slnx --configfile .\NuGet.Config
dotnet build .\Skopka.Hello.slnx -c Release --no-restore
dotnet test .\tests\Skopka.Hello.Tests -c Release --no-build
dotnet test .\tests\Skopka.Hello.IntegrationTests -c Release --no-build
docker build -f .\src\Skopka.Hello.Server\Dockerfile .
```

Integration tests require Docker because they use a real PostgreSQL
Testcontainer.
