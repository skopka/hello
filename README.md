# Skopka.Hello

[![CI](https://github.com/skopka/hello/actions/workflows/ci.yml/badge.svg)](https://github.com/skopka/hello/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/skopka/hello/blob/main/LICENSE)

Skopka.Hello is an open-source ASP.NET Core host layer for
[Skopka.Identity](https://github.com/skopka/identity). It supplies HTTP
contracts, secure browser token transport, Razor account UI, external OIDC
provider integration, host composition and policy-separated administration.
Identity users, credentials, roles, external logins,
verification and refresh-session state stay in Skopka.Identity.

The current `0.10.2` vertical slice contains:

- atomic password registration;
- automatic email, phone or user-name password login without user
  enumeration;
- authorization-code OIDC sign-in with PKCE through configured external providers;
- optional first-party OAuth/OIDC authorization server for pre-registered
  native and BFF clients with mandatory PKCE, per-client audiences, reference
  or signed-JWT access tokens and rotating reference refresh tokens;
- atomic external registration without email-based account auto-linking;
- same-origin browser/SPA OIDC APIs for sign-in, registration and one-use
  link/unlink flows without exposing provider tokens or subjects;
- configurable confirmed-email, confirmed-phone or RFC 6238 authenticator
  step-up for sensitive account and administrative actions;
- enumeration-safe email/phone confirmation and password-reset requests;
- bounded pre-lookup queuing with persistent client/target admission limits
  for anonymous account messages;
- purpose-bound email confirmation, phone confirmation and password-reset
  action tokens;
- password changes protected by a one-time code sent through the configured
  confirmed-contact channel;
- channel-aware account-message provider routing and optional bounded
  background SMTP email delivery;
- host-overridable English/Russian SMTP account-message dictionaries;
- short-lived JWT access tokens in JSON with versioned signing-key overlap;
- rotating refresh tokens in `Secure`, `HttpOnly` cookies;
- antiforgery protection for refresh and cookie logout;
- account and active-session endpoints protected by bearer authentication;
- optimistic-concurrency self-service for user name, email, phone and the
  host-defined generic profile;
- OTP-protected password setup/removal and account deletion with full session
  revocation and last-sign-in-method protection;
- Razor registration, login, account and active-session pages;
- Razor external registration and sign-in-method management pages;
- host-selectable Razor page groups, including a login-only mode;
- startup-configurable self-registration and Razor UI route prefix;
- online validation and transparent rotation for the protected UI session;
- bounded administrative user search with host-controlled profile projection;
- live role-backed read/manage/delete policies and OTP-protected block,
  unblock, soft-delete, restore and session-revocation actions;
- bounded role search, named protection levels and OTP-protected role CRUD;
- separately delegated, per-role constrained membership administration;
- Razor user/role administration pages and explicit first-administrator
  bootstrap;
- PostgreSQL persistence and packaged Skopka.Identity migrations;
- persistent versioned account/client rate limiting with bounded pruning;
- PostgreSQL-backed restart-safe account-message delivery and audit outbox in
  the ready Server;
- shared `OperationResult` application operations mapped to either RFC 9457
  `ProblemDetails` or Razor validation;
- CSS custom-property theming with optional built-in styles;
- a read-only Docker volume hook for host-provided custom CSS;
- a ready-to-run server, sample host, Docker image and Testcontainers coverage.

The sample host includes a small framework-free
[same-origin SPA reference client](samples/Skopka.Hello.Sample/README.md). It
keeps the bearer token in memory, completes OIDC navigation through an isolated
popup and demonstrates external registration plus OTP-protected link/unlink.
The same client is covered by real Chromium tests.

## Packages

| Project | Responsibility |
| --- | --- |
| `Skopka.Hello` | Facade, shared application operations, secure cookie transport and host contracts |
| `Skopka.Hello.Endpoints` | Minimal API routes, HTTP DTOs and ProblemDetails |
| `Skopka.Hello.UI` | Razor registration/login/account/session pages and theming |
| `Skopka.Hello.Oidc` | Maintained external OIDC provider adapter and validated browser flow |
| `Skopka.Hello.AuthorizationServer` | Optional OpenIddict authorization server bound to Identity logical sessions |
| `Skopka.Hello.Admin` | Policy-authorized user/role administration API/UI and safe profile projection |
| `Skopka.Hello.Server` | Executable PostgreSQL host and Docker image |

## API

| Method | Path | Authentication |
| --- | --- | --- |
| `POST` | `/auth/register` | Anonymous, when self-registration is enabled |
| `POST` | `/auth/login` | Anonymous |
| `GET` | `/connect/authorize` | Browser SSO; authorization code + PKCE when enabled |
| `POST` | `/connect/token` | Authorization-code or refresh-token grant when enabled |
| `GET` | `/auth/antiforgery` | Bearer; issues identity-bound CSRF cookies |
| `GET` | `/auth/external/providers` | Anonymous |
| `GET` | `/auth/external/{providerId}/challenge` | Anonymous browser navigation |
| `GET` | `/auth/external/{providerId}/link-challenge` | One-use link-request cookie |
| `POST` | `/auth/external/complete` | External-flow cookie + CSRF header; Bearer when linking |
| `GET` | `/auth/external/registration` | Pending external-flow cookie, when self-registration is enabled |
| `POST` | `/auth/external/registration` | Pending external-flow cookie + CSRF header, when self-registration is enabled |
| `DELETE` | `/auth/external/flow` | External-flow cookie + CSRF header |
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
| `POST` | `/account/external-logins/{providerId}/link` | Bearer + CSRF header; creates browser preflight |
| `POST` | `/account/external-logins/link/challenge` | Bearer + pending-flow cookie + CSRF header |
| `PUT` | `/account/external-logins/link` | Bearer + pending-flow cookie + CSRF header + one-time code |
| `POST` | `/account/external-logins/{providerId}/unlink/challenge` | Bearer + CSRF header |
| `DELETE` | `/account/external-logins/unlink` | Bearer + pending-flow cookie + CSRF header + one-time code |
| `DELETE` | `/account/sessions/{sessionId}` | Bearer |
| `GET` | `/account/authenticator` | Bearer |
| `POST` | `/account/authenticator/enrollment` | Bearer; returns Base32 secret, otpauth URI and QR SVG |
| `POST` | `/account/authenticator/enrollment/confirm` | Bearer + current TOTP code; returns recovery codes once |
| `POST` | `/account/authenticator/remove/challenge` | Bearer |
| `DELETE` | `/account/authenticator` | Bearer + TOTP or recovery code |
| `POST` | `/account/password/change/challenge` | Bearer |
| `POST` | `/account/password/change` | Bearer + one-time code |
| `POST` | `/account/password/set/challenge` | Bearer |
| `PUT` | `/account/password` | Bearer + one-time code |
| `POST` | `/account/password/remove/challenge` | Bearer |
| `DELETE` | `/account/password` | Bearer + one-time code |
| `POST` | `/account/delete/challenge` | Bearer |
| `DELETE` | `/account` | Bearer + one-time code |
| `GET` | `/admin/users` | Bearer + current read or role-assignment membership |
| `POST` | `/admin/users/{userId}/actions/{action}/challenge` | Bearer + current manage/delete-role membership |
| `POST` | `/admin/users/{userId}/actions/{action}` | Bearer + current manage/delete-role membership + one-time code |
| `GET` | `/admin/roles` | Bearer + current read or role-assignment membership |
| `GET` | `/admin/users/{userId}/roles` | Bearer + current read or role-assignment membership |
| `POST` | `/admin/roles/actions/{action}/challenge` | Bearer + current delete-role membership for CRUD, or role-assignment membership for assign/remove |
| `POST` | `/admin/roles/actions/{action}` | Same live role policy + one-time code |

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

Hosts that only need the packaged sign-in page can select it when registering
the UI:

```csharp
services.AddSkopkaHelloUi<Profile, ProfileFactory>(options =>
{
    options.EnabledPages = HelloUiPages.Login;
    options.AuthenticatedRedirectPath = "/admin";
});
```

`EnabledPages` defaults to `HelloUiPages.All`. Disabled Razor pages remain in
the package assembly, but receive no HTTP route and return 404. Sessions and
account-security groups require Account; ExternalIdentity also requires
Account. Every enabled group requires Login. When self-registration is
disabled through `SkopkaHelloOptions`, registration routes remain absent even
if `HelloUiPages.Registration` is selected. A login-only configuration must set
`AuthenticatedRedirectPath` to a local absolute path; a valid local
`ReturnUrl` still takes priority after sign-in.

Built-in password and external registration fields can independently be
`Hidden`, `Optional` or `Required` through `options.Registration`. Display
name is required by default, email/user name/phone are optional, and the
profile locale is hidden. Hidden values are also discarded from crafted form
posts. See [UI customization](docs/customization.md#registration-fields) for
email-only, phone-only and user-name-only configurations.

The optional UI localizer ships English and Russian catalogs without changing
route paths:

```csharp
services.AddSkopkaHelloUi<Profile, ProfileFactory>(options =>
{
    options.ApplicationHomeUrl = "https://app.example.com/";
    options.LayoutPath = "/Pages/Shared/_Layout.cshtml";
    options.NoticeText =
        "Test environment: data may be removed without notice.";
    options.TermsOfServiceUrl = "/terms";
    options.PrivacyPolicyUrl = "https://legal.example.com/privacy";
    options.Localization.Enabled = true;
    options.Localization.DefaultCulture = "ru";
    options.Localization.UseAcceptLanguageHeader = false;
    options.Localization.RemoveCulture("en");
    options.Localization.AddDictionaryFile(
        "ru",
        "Localization/skopka-hello.ru.override.json");
});
```

`ApplicationHomeUrl` adds a localized "Return to application" link to the
packaged header. It accepts a safe local absolute path or an absolute HTTPS URL
without credentials, query or fragment; leave it unset when the identity UI is
the application itself.

`NoticeText` renders a host-owned message above the content of every packaged
Hello page. Null, empty and whitespace-only values preserve the existing
markup. Razor HTML-encodes the value, and host CSS can target `.hello-notice`.
The ready Server reads `SkopkaHello:Ui:NoticeText`.

`LayoutPath` places both Hello and Admin Razor page bodies inside a compiled
host layout. Null preserves the packaged shells. Host layouts own the document
title, navigation, footer, notices and resource links; use
`HelloUiDefaults.BuiltInStylesheetPath` for the public `hello.css` URL and keep
sections that package pages do not declare optional.

`TermsOfServiceUrl` and `PrivacyPolicyUrl` add localized links to the packaged
footer and a separate required consent checkbox for each configured document
on both password and external registration forms. The same application policy
also requires `acceptTermsOfService` and/or `acceptPrivacyPolicy` on headless
registration requests, so `/auth/register` cannot bypass the UI rule. Hello
captures the accepted flags with a server timestamp and exposes the evidence
to `IHelloUiProfileFactory.Create`; hosts can persist it atomically in their
profile. `IHelloRegistrationConsentProfileEnricher<TProfile>` provides the same
trusted overwrite point for API-bound profiles. The host still owns document
content, revisions and retention. Each URL accepts the same safe local absolute
path or absolute HTTPS shape as `ApplicationHomeUrl`.

The footer selector persists the supported culture in a secure preference
cookie and is omitted when only one culture remains. Use `RemoveCulture` or
`SetSupportedCultures` for a single-language host. Even with localization
disabled, Hello applies `DefaultCulture` to its Razor requests and emits the
matching `Content-Language`, so the document `lang` never depends on the
machine culture. Custom JSON files can add a culture or partially override
stable text keys; details are in
[UI localization](docs/customization.md#ui-localization).

Authenticator support is opt-in for a custom host at both composition layers:

```csharp
var identity = services.AddSkopkaHello<Profile>(options =>
{
    options.Totp.Enabled = true;
    options.Totp.Issuer = "IqZone XYZ";
});

identity.UseDataProtectionTotp();

services.AddSkopkaHelloDelivery(options =>
{
    options.VerificationChannel = HelloDeliveryChannel.Email;
    options.RequireTotpWhenEnabled = true;
});
```

The factor uses the standard authenticator-app profile (Base32 secret,
HMAC-SHA1, six digits, 30 seconds), accepts one adjacent clock step, rejects
counter replay and includes one-use recovery codes. When
`RequireTotpWhenEnabled` is true, TOTP replaces confirmed-contact delivery for
users who have enabled it; other users retain the configured email/SMS flow.
Enrollment, recovery-code display and confirmed removal live on
`/hello/account/security`. Removing one’s own factor and resetting another
user’s factor from admin both require step-up and revoke the affected sessions.

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
| `/hello/account/change-password` | Change password after the configured step-up |
| `/hello/account/security` | Manage password, authenticator and account deletion after step-up |
| `/hello/account/external-logins` | Link and unlink external providers after the configured step-up |
| `/hello/admin/users` | Search and administer users after current role-policy authorization |
| `/hello/admin/roles` | Search and administer roles after current role-policy authorization |
| `/hello/culture` | Antiforgery-protected UI culture selection when localization is enabled |

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

See [getting started](https://github.com/skopka/hello/blob/main/docs/getting-started.md)
for configuration and requests,
[administration](https://github.com/skopka/hello/blob/main/docs/administration.md)
for policy, projection and bootstrap,
[architecture](https://github.com/skopka/hello/blob/main/docs/architecture.md)
for boundaries and
[authorization server](https://github.com/skopka/hello/blob/main/docs/authorization-server.md)
for native/BFF OAuth configuration and limits,
[mail OIDC integration](https://github.com/skopka/hello/blob/main/docs/mail-oidc-integration.md)
for Roundcube and Stalwart interoperability,
[customization](https://github.com/skopka/hello/blob/main/docs/customization.md)
for custom CSS volumes, and
[security](https://github.com/skopka/hello/blob/main/docs/security.md) before
deploying. Coordinated package publication is documented in
[releasing](https://github.com/skopka/hello/blob/main/docs/releasing.md).

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

Licensed under the
[Apache License 2.0](https://github.com/skopka/hello/blob/main/LICENSE). The
software is provided on an “AS IS” basis, without warranties or conditions of
any kind.

Do not report vulnerabilities in public issues. Follow
[SECURITY.md](https://github.com/skopka/hello/blob/main/SECURITY.md).
