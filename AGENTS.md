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
ProblemDetails contract and do not turn them into HTTP 500 responses.

## Current vertical

The implemented endpoints are register, login, refresh, logout, logout-all,
current account, active sessions and revoke-by-id. Registration must remain
atomic through `IIdentityRegistrationService<TProfile>`. Endpoints may call
identity application services, never EF stores.

Refresh tokens stay only in `Secure`, `HttpOnly` cookies. Cookie-authorized
mutations require antiforgery validation. Access tokens are JSON/bearer tokens.
Client keys come from trusted server request context, not request DTOs.

## Modules

- `src/Skopka.Hello` — facade, DI, options, request context, event/outbox
  contracts.
- `src/Skopka.Hello.Endpoints` — Minimal API, DTOs, cookie transport and
  ProblemDetails.
- `src/Skopka.Hello.UI` — future Razor Class Library, no identity business
  logic.
- `src/Skopka.Hello.Oidc` — future maintained OAuth/OIDC adapter, never a
  home-grown protocol.
- `src/Skopka.Hello.Admin` — future authorized admin workflows.
- `src/Skopka.Hello.Server` — executable composition and Docker image.

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
