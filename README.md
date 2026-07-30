# Skopka.Hello

Skopka.Hello is an open-source ASP.NET Core host layer for
[Skopka.Identity](https://github.com/skopka/identity). It supplies HTTP
contracts, secure browser token transport, host composition and future package
boundaries for account UI, administration and OAuth/OIDC. Identity users,
credentials, roles, external logins, verification and refresh-session state stay
in Skopka.Identity.

The current `0.1.0` vertical slice contains:

- atomic password registration;
- password login without user enumeration;
- short-lived JWT access tokens in JSON;
- rotating refresh tokens in `Secure`, `HttpOnly` cookies;
- antiforgery protection for refresh and cookie logout;
- account and active-session endpoints protected by bearer authentication;
- PostgreSQL persistence and packaged Skopka.Identity migrations;
- one `OperationResult` to RFC 9457 `ProblemDetails` mapping;
- a ready-to-run server, sample host, Docker image and Testcontainers coverage.

OAuth/OIDC, Razor UI and administration have explicit package boundaries but are
intentionally deferred beyond this first vertical slice.

## Packages

| Project | Responsibility |
| --- | --- |
| `Skopka.Hello` | Facade, `AddSkopkaHello<TProfile>()`, options and host contracts |
| `Skopka.Hello.Endpoints` | Minimal API routes, HTTP DTOs and ProblemDetails |
| `Skopka.Hello.UI` | Razor Class Library boundary for future account UI |
| `Skopka.Hello.Oidc` | Future authorization-server/provider adapter boundary |
| `Skopka.Hello.Admin` | Future administration API/UI boundary |
| `Skopka.Hello.Server` | Executable PostgreSQL host and Docker image |

## API

| Method | Path | Authentication |
| --- | --- | --- |
| `POST` | `/auth/register` | Anonymous |
| `POST` | `/auth/login` | Anonymous |
| `POST` | `/auth/refresh` | Refresh cookie + CSRF header |
| `POST` | `/auth/logout` | Refresh cookie + CSRF header |
| `POST` | `/auth/logout-all` | Bearer |
| `GET` | `/account/me` | Bearer |
| `GET` | `/account/sessions` | Bearer |
| `DELETE` | `/account/sessions/{sessionId}` | Bearer |

Registration calls
`IIdentityRegistrationService<TProfile>.RegisterPasswordAsync`; it is never
implemented as separate user creation and password mutation. Login calls
`IPasswordAuthenticationService<TProfile>`, then creates a session through
`IIdentitySessionService<TProfile>`.

See [getting started](docs/getting-started.md) for configuration and requests,
[architecture](docs/architecture.md) for boundaries and
[security](docs/security.md) before deploying.

## Build and test

When developing beside the Skopka.Identity repository, copy its locally packed
NuGet artifacts into the ignored local feed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\sync-local-identity-packages.ps1
dotnet restore .\Skopka.Hello.slnx --configfile .\NuGet.Config
dotnet build .\Skopka.Hello.slnx -c Release --no-restore
dotnet test .\tests\Skopka.Hello.Tests -c Release --no-build
dotnet test .\tests\Skopka.Hello.IntegrationTests -c Release --no-build
```

The integration project requires a running Docker Engine and starts an isolated
PostgreSQL container.

## License and security

Licensed under the [Apache License 2.0](LICENSE). The software is provided on an
“AS IS” basis, without warranties or conditions of any kind.

Do not report vulnerabilities in public issues. Follow
[SECURITY.md](SECURITY.md).
