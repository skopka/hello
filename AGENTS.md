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

The implemented surfaces are registration, password and external OIDC login,
sessions, account, password-reset, email/phone-confirmation, external-login
management and step-up password-change Minimal APIs plus their Razor UI, and
the bounded role-authorized user/role administration API/UI.
Registration must remain atomic through
`IIdentityRegistrationService<TProfile>` for both password and external
registration. HTTP handlers call
`IHelloIdentityApplication<TProfile>` or Skopka.Identity application services,
never EF stores.

Hosts can select Razor page groups through `SkopkaHelloUiOptions.EnabledPages`.
Disabled pages stay in the UI package but receive no selectors, links or
reachable handlers. Login-only hosts must provide a safe local authenticated
redirect; the default remains the complete UI surface.

Optional Razor localization is owned by `Skopka.Hello.UI`, keeps culture out
of the immutable UI routes and resolves a supported preference cookie before
`Accept-Language` and the configured default. English and Russian are packaged;
explicit server-side JSON dictionaries may add or override cultures. Admin UI
contributes to the same catalog. Dictionary files are startup inputs, never
static content.

Self-registration is a startup policy owned by `SkopkaHelloOptions`. When it is
disabled, password and external application operations fail before calling
Identity and the built-in registration API/Razor selectors are not mapped.
Existing sign-in and explicit external link/unlink remain available. The Razor
UI prefix is also configured once through `AddSkopkaHello<TProfile>`; core
action links, UI routing and OIDC browser redirects consume the same immutable
route snapshot. It is not the host application's `PathBase`.

Registration consent is a shared application invariant, not a Razor-only
check. UI legal-document URLs contribute requirements to password, headless and
external registration. Transports provide acceptance flags; Hello attaches a
server timestamp and exposes trusted evidence through the UI profile factory
and optional profile enricher before the atomic Identity registration call.

Refresh tokens stay only in `Secure`, `HttpOnly` cookies. Cookie-authorized
mutations require antiforgery validation. API access tokens are returned as
JSON/bearer tokens. The protected Razor UI ticket carries its access token in
an encrypted `HttpOnly` cookie and validates it online. Client keys come from
trusted server request context, not request DTOs.

The ready Server enables persistent account/client rate limiting with
versioned HMAC keys from configuration. Production key material stays outside
source; the public test-only Development example is excluded from publish and
Docker output. Rotation retains overlapping versions, and a bounded worker
prunes old buckets.

The ready Server also replaces the facade's best-effort anonymous inbox and
SMTP queue with PostgreSQL inbox/outbox delivery by default. Sensitive targets,
action URLs and OTPs are protected with the shared Data Protection key ring.
Workers use leases and `SKIP LOCKED`; delivery is at-least-once. Identity
security events are copied post-commit to the Hello audit outbox, without
claiming cross-transaction atomicity.

Anonymous account-message requests suppress exact-lookup not-found results and
return the same accepted response for every well-formed email or phone. Links use a
configured public origin, confirmation GET requests never mutate state, and
delivery stays behind `IHelloAccountMessageSender`.

Password change requires an online-validated session, a confirmed contact for
the configured delivery channel and Identity-owned OTP step-up. User, action,
binding and optimistic version are server-derived; the OTP is delivered out of
band and never returned by HTTP. A challenge never silently changes channel.

External OIDC uses one maintained ASP.NET Core handler per configured provider.
The handler owns state, nonce, PKCE, code redemption and token validation.
Only the configured provider id and validated subject reach Skopka.Identity;
provider tokens and subjects never reach UI/API responses. Matching email never
links accounts. Link and unlink require an online session, a confirmed-contact OTP,
an exact provider/subject binding and a fresh optimistic snapshot. They preserve
at least one enabled sign-in method, revoke old sessions and issue a fresh one.
Terminal external and pending POSTs atomically consume a protected flow id. The
ready Server backs this replay guard with the persistent HMAC rate limiter;
hosts without one use the bounded process-local fallback or replace
`IHelloOidcFlowStore` with a shared atomic implementation.

The optional authorization server uses OpenIddict authorization code with
mandatory PKCE for pre-registered native and BFF clients. Authorization codes
and rotating refresh tokens remain reference tokens. Access tokens are opaque
reference tokens by default or explicitly configured signed, unencrypted JWTs
for offline resource-server validation. Code redemption creates a distinct
Identity logical session; Hello access/refresh authentication validates its
`sid` online. Client configuration is synchronized only by the migration
command.

## Modules

- `src/Skopka.Hello` - facade, shared identity application operations, cookie
  transport, request context and event/outbox contracts.
- `src/Skopka.Hello.Endpoints` - Minimal API, DTOs and ProblemDetails.
- `src/Skopka.Hello.UI` - Razor registration/login/account/session pages and
  theming, with no identity business logic.
- `src/Skopka.Hello.Oidc` - maintained external OIDC client adapter and
  validated pending browser flow, never a home-grown protocol or authorization
  server.
- `src/Skopka.Hello.AuthorizationServer` - optional OpenIddict-based OAuth 2.0
  and OpenID Connect authorization server for pre-registered native and BFF
  clients, bound to Identity logical sessions.
- `src/Skopka.Hello.Admin` - bounded user and role queries, safe profile
  projection, live role-policy authorization and step-up-protected user and
  role administration.
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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Skopka.Hello.BrowserTests\bin\Release\net10.0\playwright.ps1 install chromium
dotnet test .\tests\Skopka.Hello.BrowserTests -c Release --no-build
docker build -f .\src\Skopka.Hello.Server\Dockerfile .
```

Integration tests require Docker because they use a real PostgreSQL
Testcontainer. Browser tests require the Playwright Chromium version installed
by the generated script; on PowerShell 7 hosts use `pwsh` instead of
`powershell.exe`.

CI must restore through the repository `NuGet.Config`, build, run all test
projects including the real PostgreSQL Testcontainer and Chromium browser
scenarios, audit NuGet dependencies
and pack all six source packages without publishing them. Tag pushes matching
`v*` publish `Skopka.Hello`, `.Admin`, `.AuthorizationServer`, `.Endpoints`,
`.Oidc` and `.UI` together
through `.github/workflows/release.yml`. Keep the tag-derived version, exact
package-set validation, NuGet.org publication and GitHub Release attachments in
one coordinated job. The same tag publishes the ready Server image to GHCR
with its exact SemVer and commit-SHA tags. Release setup and operator steps
live in `docs/releasing.md`.
