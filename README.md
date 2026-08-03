# Skopka.Hello

[![CI](https://github.com/skopka/hello/actions/workflows/ci.yml/badge.svg)](https://github.com/skopka/hello/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/skopka/hello/blob/main/LICENSE)

Skopka.Hello is an open-source ASP.NET Core host layer for
[Skopka.Identity](https://github.com/skopka/identity). It supplies HTTP
contracts, secure browser token transport, Razor account UI, external OIDC
provider integration, host composition and a package boundary for
administration. Identity users, credentials, roles, external logins,
verification and refresh-session state stay in Skopka.Identity.

The current `0.4.0` vertical slice contains:

- atomic password registration;
- automatic email, phone or user-name password login without user
  enumeration;
- authorization-code OIDC sign-in with PKCE through configured external providers;
- atomic external registration without email-based account auto-linking;
- configurable confirmed-email or confirmed-phone OTP step-up for external
  login link and unlink;
- enumeration-safe email/phone confirmation and password-reset requests;
- bounded pre-lookup queuing with persistent client/target admission limits
  for anonymous account messages;
- purpose-bound email confirmation, phone confirmation and password-reset
  action tokens;
- password changes protected by a one-time code sent through the configured
  confirmed-contact channel;
- channel-aware account-message provider routing and optional bounded
  background SMTP email delivery;
- short-lived JWT access tokens in JSON;
- rotating refresh tokens in `Secure`, `HttpOnly` cookies;
- antiforgery protection for refresh and cookie logout;
- account and active-session endpoints protected by bearer authentication;
- optimistic-concurrency self-service for user name, email, phone and the
  host-defined generic profile;
- OTP-protected password setup/removal and account deletion with full session
  revocation and last-sign-in-method protection;
- Razor registration, login, account and active-session pages;
- Razor external registration and sign-in-method management pages;
- startup-configurable self-registration and Razor UI route prefix;
- online validation and transparent rotation for the protected UI session;
- PostgreSQL persistence and packaged Skopka.Identity migrations;
- persistent versioned account/client rate limiting with bounded pruning;
- shared `OperationResult` application operations mapped to either RFC 9457
  `ProblemDetails` or Razor validation;
- CSS custom-property theming with optional built-in styles;
- a read-only Docker volume hook for host-provided custom CSS;
- a ready-to-run server, sample host, Docker image and Testcontainers coverage.

An OAuth/OIDC authorization server and administration surfaces remain deferred.

## Packages

| Project | Responsibility |
| --- | --- |
| `Skopka.Hello` | Facade, shared application operations, secure cookie transport and host contracts |
| `Skopka.Hello.Endpoints` | Minimal API routes, HTTP DTOs and ProblemDetails |
| `Skopka.Hello.UI` | Razor registration/login/account/session pages and theming |
| `Skopka.Hello.Oidc` | Maintained external OIDC provider adapter and validated browser flow |
| `Skopka.Hello.Admin` | Future administration API/UI boundary |
| `Skopka.Hello.Server` | Executable PostgreSQL host and Docker image |

## API

| Method | Path | Authentication |
| --- | --- | --- |
| `POST` | `/auth/register` | Anonymous, when self-registration is enabled |
| `POST` | `/auth/login` | Anonymous |
| `GET` | `/auth/external/providers` | Anonymous |
| `POST` | `/auth/refresh` | Refresh cookie + CSRF header |
| `POST` | `/auth/logout` | Refresh cookie + CSRF header |
| `POST` | `/auth/logout-all` | Bearer |
| `POST` | `/auth/password-reset/request` | Anonymous |
| `POST` | `/auth/password-reset/confirm` | Anonymous action token |
| `POST` | `/auth/email-confirmation/request` | Anonymous |
| `POST` | `/auth/email-confirmation/confirm` | Anonymous action token |
| `POST` | `/auth/phone-confirmation/request` | Anonymous |
| `POST` | `/auth/phone-confirmation/confirm` | Anonymous action token |
| `GET` | `/account/me` | Bearer |
| `PUT` | `/account/user-name` | Bearer |
| `PUT` | `/account/email` | Bearer |
| `PUT` | `/account/phone` | Bearer |
| `PUT` | `/account/profile` | Bearer |
| `GET` | `/account/sessions` | Bearer |
| `GET` | `/account/external-logins` | Bearer |
| `DELETE` | `/account/sessions/{sessionId}` | Bearer |
| `POST` | `/account/password/change/challenge` | Bearer |
| `POST` | `/account/password/change` | Bearer + one-time code |
| `POST` | `/account/password/set/challenge` | Bearer |
| `PUT` | `/account/password` | Bearer + one-time code |
| `POST` | `/account/password/remove/challenge` | Bearer |
| `DELETE` | `/account/password` | Bearer + one-time code |
| `POST` | `/account/delete/challenge` | Bearer |
| `DELETE` | `/account` | Bearer + one-time code |

When `SkopkaHelloOptions.SelfRegistrationEnabled` is false, password and
external self-registration operations return the shared
`hello.registration.disabled` result and the built-in registration API and UI
routes are not mapped. Existing password and external sign-in, recovery and
account linking remain available. Registration calls
`IIdentityRegistrationService<TProfile>.RegisterPasswordAsync`; it is never
implemented as separate user creation and password mutation. Login calls
`IPasswordAuthenticationService<TProfile>`, then creates a session through
`IIdentitySessionService<TProfile>`.

## Browser UI

The ready server uses `/hello` as its default UI route prefix. Hosts can set
`SkopkaHelloOptions.UiPathPrefix` while calling `AddSkopkaHello<TProfile>`;
every Razor route, internal OIDC redirect and account-message action link is
then derived from that value. API routes and the provider callback
`/signin-skopka-oidc/{provider}` remain root-relative. The prefix must be a
non-empty absolute path other than `/`.

With the default prefix, the ready server exposes:

| Path | Purpose |
| --- | --- |
| `/hello/register` | Atomic password registration |
| `/hello/login` | Password login |
| `/hello/external/complete` | Explicit POST completion after a validated provider callback |
| `/hello/external/register` | Atomic registration using a pending validated external identity |
| `/hello/forgot-password` | Request a password-reset link |
| `/hello/reset-password` | Apply a password-reset token |
| `/hello/resend-confirmation` | Request an email-confirmation link |
| `/hello/confirm-email` | Confirm email through an automatic antiforgery POST with a manual fallback |
| `/hello/resend-phone-confirmation` | Request a phone-confirmation SMS |
| `/hello/confirm-phone` | Confirm a phone after an explicit POST |
| `/hello/account` | Current account summary |
| `/hello/account/sessions` | List and revoke active sessions |
| `/hello/account/change-password` | Change password after configured-channel OTP step-up |
| `/hello/account/security` | Set/remove a password or delete the account after OTP step-up |
| `/hello/account/external-logins` | Link and unlink external providers after configured-channel OTP step-up |

Account and credential API and Razor handlers share
`IHelloIdentityApplication<TProfile>`;
external flows use the parallel `IHelloExternalIdentityApplication<TProfile>`
through the OIDC adapter. The UI uses an encrypted `HttpOnly` authentication
cookie, keeps the refresh token in its separate `HttpOnly` cookie, validates the
access token online and rotates the refresh session after access-token expiry.

External provider redirects use ASP.NET Core's maintained OpenID Connect handler
with authorization code, PKCE, state, nonce and normal token validation. Only
the configured provider id and validated `sub` reach Skopka.Identity. A matching
email never links accounts. Link and unlink rotate the security stamp, revoke
the old sessions and issue a fresh session to the current browser. Protected
flow ids make terminal external submissions one-use; the ready Server persists
that replay guard through its HMAC-backed rate limiter.

See [getting started](docs/getting-started.md) for configuration and requests,
[architecture](docs/architecture.md) for boundaries and
[customization](docs/customization.md) for custom CSS volumes, and
[security](docs/security.md) before deploying. Coordinated package publication
is documented in [releasing](docs/releasing.md).

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
