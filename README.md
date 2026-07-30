# Skopka.Hello

Skopka.Hello is an open-source ASP.NET Core host layer for
[Skopka.Identity](https://github.com/skopka/identity). It supplies HTTP
contracts, secure browser token transport, Razor account UI, host composition
and package boundaries for administration and OAuth/OIDC. Identity users,
credentials, roles, external logins, verification and refresh-session state stay
in Skopka.Identity.

The current `0.1.0` vertical slice contains:

- atomic password registration;
- password login without user enumeration;
- enumeration-safe email confirmation and password-reset requests;
- purpose-bound email confirmation and password-reset action tokens;
- optional bounded background SMTP delivery;
- short-lived JWT access tokens in JSON;
- rotating refresh tokens in `Secure`, `HttpOnly` cookies;
- antiforgery protection for refresh and cookie logout;
- account and active-session endpoints protected by bearer authentication;
- Razor registration, login, account and active-session pages;
- online validation and transparent rotation for the protected UI session;
- PostgreSQL persistence and packaged Skopka.Identity migrations;
- shared `OperationResult` application operations mapped to either RFC 9457
  `ProblemDetails` or Razor validation;
- CSS custom-property theming with optional built-in styles;
- a read-only Docker volume hook for host-provided custom CSS;
- a ready-to-run server, sample host, Docker image and Testcontainers coverage.

OAuth/OIDC, step-up verification UI and administration remain deferred.

## Packages

| Project | Responsibility |
| --- | --- |
| `Skopka.Hello` | Facade, shared application operations, secure cookie transport and host contracts |
| `Skopka.Hello.Endpoints` | Minimal API routes, HTTP DTOs and ProblemDetails |
| `Skopka.Hello.UI` | Razor registration/login/account/session pages and theming |
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
| `POST` | `/auth/password-reset/request` | Anonymous |
| `POST` | `/auth/password-reset/confirm` | Anonymous action token |
| `POST` | `/auth/email-confirmation/request` | Anonymous |
| `POST` | `/auth/email-confirmation/confirm` | Anonymous action token |
| `GET` | `/account/me` | Bearer |
| `GET` | `/account/sessions` | Bearer |
| `DELETE` | `/account/sessions/{sessionId}` | Bearer |

Registration calls
`IIdentityRegistrationService<TProfile>.RegisterPasswordAsync`; it is never
implemented as separate user creation and password mutation. Login calls
`IPasswordAuthenticationService<TProfile>`, then creates a session through
`IIdentitySessionService<TProfile>`.

## Browser UI

The ready server exposes:

| Path | Purpose |
| --- | --- |
| `/hello/register` | Atomic password registration |
| `/hello/login` | Password login |
| `/hello/forgot-password` | Request a password-reset link |
| `/hello/reset-password` | Apply a password-reset token |
| `/hello/resend-confirmation` | Request an email-confirmation link |
| `/hello/confirm-email` | Confirm an email after an explicit POST |
| `/hello/account` | Current account summary |
| `/hello/account/sessions` | List and revoke active sessions |

API and Razor handlers share `IHelloIdentityApplication<TProfile>`. The UI uses
an encrypted `HttpOnly` authentication cookie, keeps the refresh token in its
separate `HttpOnly` cookie, validates the access token online and rotates the
refresh session after access-token expiry.

See [getting started](docs/getting-started.md) for configuration and requests,
[architecture](docs/architecture.md) for boundaries and
[customization](docs/customization.md) for custom CSS volumes, and
[security](docs/security.md) before deploying.

## Build and test

Restore the published Skopka.Identity packages from NuGet:

```powershell
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
