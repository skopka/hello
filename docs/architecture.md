# Architecture

Skopka.Hello is the ASP.NET Core layer above Skopka.Identity. It does not fork or
reimplement the identity domain.

## Dependency direction

```text
Server / Sample
  -> Skopka.Hello.Endpoints
  -> Skopka.Hello.UI
  -> Skopka.Hello.Oidc
  -> Skopka.Hello.AuthorizationServer
  -> Skopka.Hello.Admin

Endpoints / UI
  -> Skopka.Hello.Oidc

Endpoints / UI / Oidc / AuthorizationServer
  -> Skopka.Hello

Skopka.Hello
  -> Skopka.Identity packages

Admin
  -> Skopka.Hello
  -> Skopka.Hello.Endpoints
  -> Skopka.Hello.UI
```

`Skopka.Hello` calls `AddSkopkaIdentity<TProfile>()` and returns its
`IdentityBuilder<TProfile>`. The host then selects PostgreSQL, a password hasher,
JWT sessions and bearer validation explicitly:

```csharp
var identity = services
    .AddSkopkaHello<MyProfile>(options =>
    {
        options.SelfRegistrationEnabled = true;
        options.UiPathPrefix = "/hello";
    })
    .UsePostgreSql(connectionString)
    .UsePbkdf2PasswordHasher()
    .UseDataProtectionActionTokens()
    .UseJwtSessions(currentJwtKeyId, versionedJwtKeys, jwt =>
    {
        jwt.Issuer = issuer;
        jwt.Audience = audience;
    });

identity.UseHmacRateLimiting(
    currentVersion,
    versionedRateLimitKeys);
identity.UseHmacOneTimeCodes(
    verificationKeyProvider);
identity.UseJwtBearerAuthentication();

services.AddSkopkaHelloOidc<MyProfile>(options =>
{
    externalOidcConfiguration.Bind(options);
    options.PublicOrigin = publicOrigin;
    options.SecureCookies = secureCookies;
});
```

## Shared application flows

`IHelloIdentityApplication<TProfile>` is the shared transport-facing
orchestrator. Registration maps to
`RegisterPasswordUserCommand<TProfile>` and uses the atomic registration
service. Login always uses Identity's single automatic identifier lookup; the
caller cannot select a handle type or multiply per-account rate-limit buckets.
It then passes the returned user id and current security stamp to session creation. Refresh
delegates strict rotation to Skopka.Identity. Minimal API and Razor handlers
call the same operations and never call EF stores directly.

Authenticated account edits also flow through this generic application
boundary. User name, email and phone mutations derive the user from an
online-validated access token and pass the caller's `ExpectedVersion` to
Identity. Profile replacement carries the host's exact `TProfile`; Hello does
not inspect or persist its schema. The optional Razor
`IHelloUiProfileEditor<TProfile>` only maps host-declared form fields to that
typed value and returns structured `OperationResult` validation failures.

`AddSkopkaHello<TProfile>` validates the UI prefix and registers one immutable
`HelloUiRoutePaths` snapshot. Core action links, Razor route conventions and
OIDC browser redirects consume that same snapshot, so hosts cannot configure
different paths for the three layers. Disabling self-registration gates both
password and external application operations before Identity is called and
removes their public Minimal API/Razor selectors. Administrative registration
is outside this self-service policy.

Password-reset and email/phone-confirmation requests validate and normalize the
target, consume persistent client and target rate-limit partitions, and enter
`IHelloAnonymousAccountMessageInbox` before any account lookup or action-token
work. The reusable package defaults to a bounded in-memory implementation; the
ready Server replaces it with a PostgreSQL lease-based inbox.
The request therefore returns from the same pipeline for known and unknown
targets. A denied rate-limit decision or full queue is silently dropped after
validation so every well-formed request still receives `202 Accepted`. A worker
creates a new dependency-injection scope for every queued item and uses the
exact normalized `IIdentityUserLookupService<TProfile>` contract,
suppresses not-found, issues the purpose-bound Identity action token, builds the
link from configured `PublicOrigin` and hands the message to
`IHelloAccountMessageSender`. The built-in dispatcher selects one
`IHelloAccountMessageProvider` by configured provider id and semantic channel.
Provider ids are unique and the selected provider must report the matching
channel; invalid routing fails at startup. The reusable SMTP provider defaults
to its own bounded background queue. In the ready Server, a PostgreSQL outbox
wraps the configured email route and SMTP runs directly behind its worker, so
the row is acknowledged only after the provider reports success. Custom hosts
can still replace either persistence contract or register an SMS provider
without moving delivery into Identity.
Phone-confirmation messages always use SMS. Step-up messages use the configured
`VerificationChannel`; Hello selects and validates the confirmed destination
before it creates the challenge and never performs cross-channel fallback.

Authenticated password change validates the access token online, derives the
user id, optimistic-concurrency version, action and binding on the server, and
uses Skopka.Identity step-up verification before calling
`IPasswordCredentialService<TProfile>.ChangePasswordAsync`. API and Razor UI
share this operation. The transport receives only a safe challenge id, expiry
and delivery channel; the OTP is sent through the provider configured for that
channel. A successful change revokes all sessions after Identity rotates the
security stamp.
Password change, external link and external unlink use distinct semantic
message kinds so providers cannot render a misleading shared step-up template.

Password setup, password removal and self-service account deletion use the
same Identity-owned step-up mechanism with distinct actions and bindings.
Hello reads the current sign-in-method snapshot before password removal and
again at completion, so a password cannot become the last method removed by a
stale flow. Successful credential mutations or deletion revoke every session;
the UI discards its protected ticket and all session cookies.

## Administration flow

`IHelloAdminApplication` is deliberately non-generic at its transport boundary,
but its implementation is composed for the host's `TProfile`. It queries users
only through the bounded cursor-based
`IIdentityUserQueryService<TProfile>` and passes every profile through the
mandatory `IHelloAdminProfileProjector<TProfile>`. Raw profile values never
reach an admin DTO or Razor model.

Admin authentication, current role-policy authorization and Identity step-up
are evaluated separately. Mutations bind the actor, target, exact action,
optimistic version and action parameters into the one-time proof. Block and
delete also revoke target sessions after the state mutation. The API and Razor
page call the same application methods and map expected `OperationResult`
failures without using EF stores.

## External OIDC flow

`Skopka.Hello.Oidc` registers one named ASP.NET Core OpenID Connect scheme per
enabled provider and does not replace the default bearer scheme. The maintained
handler owns discovery, state, correlation, nonce, authorization-code exchange,
PKCE and token validation. Provider access, refresh and ID tokens are not saved.
After validation, the adapter copies only a bounded configured provider id,
exact case-sensitive `sub` and optional display hints into a short-lived
encrypted external ticket.

The raw callback is derived from the normalized provider id:

```text
{SkopkaHello:PublicOrigin}/signin-skopka-oidc/{provider-id}
```

The Razor flow redirects to `{UiPathPrefix}/external/complete`; a same-origin
browser/SPA flow may instead supply a validated local application landing path
through `/auth/external/{providerId}/challenge`. Both perform a separate
antiforgery-protected POST before resolving the external identity, registering
an account or retaining a pending link. This two-stage flow also ensures strict
same-site local cookies are available again after the cross-site provider
callback. The headless response contains only a local `SessionResponse` or safe
registration hints; provider protocol tokens and subjects remain inside the
adapter.

Headless linking adds an authenticated preflight because a top-level browser
navigation cannot attach a Bearer header. A Bearer- and antiforgery-protected
POST writes a short-lived HttpOnly Strict link-request cookie bound to the
validated user, session, provider and local return path. It returns only a
local challenge URL. Navigating there atomically consumes the preflight flow id
and creates the normal OIDC challenge. Completion requires the original Bearer
session, promotes the provider result to the ordinary pending link ticket and
then uses the same Identity OTP step-up as Razor UI.

External and pending tickets also contain an unpredictable flow id. The
terminal POST atomically consumes it before session creation or account
mutation. The default `IHelloOidcFlowStore` reuses the persistent Identity rate
limiter when available and falls back to a bounded process-local store. A
retryable failure rotates the id without extending the ticket deadline.

When self-registration is enabled, an unknown external identity opens
`{UiPathPrefix}/external/register` and is persisted atomically through
`RegisterExternalAsync`. When disabled, the external ticket is cleared and the
shared `hello.registration.disabled` result is returned instead. A
provider-verified email may prefill
the form, but remains unconfirmed locally. Matching an existing email never
authorizes linking; the user must sign in to that account and start an explicit
link from `{UiPathPrefix}/account/external-logins`.

Link and unlink require an online-validated local session, a confirmed contact
for the configured channel and an Identity-owned OTP bound to the exact
provider/subject operation, delivery channel and confirmed-destination
fingerprint. The pending protected ticket binds the local user, session and
challenge id. Completion reads a fresh sign-in-method snapshot, rechecks that
unlink retains another enabled method and uses that current version for the
Identity compare-and-swap mutation. Unrelated profile edits while the OTP is
pending therefore remain valid, while a later race still fails safely. A
successful mutation rotates the Identity security stamp; Hello revokes all
prior sessions and creates a fresh session for the current browser. A terminal
challenge or authorization failure before mutation clears the pending OIDC
flow but retains the local browser session. A failure after the mutation is
attempted also clears that session and requires a new login because the account
state may already have changed. Only a wrong OTP response remains retryable
with the same challenge.

## OAuth/OIDC authorization server

`Skopka.Hello.AuthorizationServer` uses OpenIddict for discovery, protocol
validation, authorization codes, PKCE, token protection and reference-token
storage. It does not parse OAuth messages or redeem codes itself. The package
supports pre-registered first-party public native and confidential BFF clients;
the ready Server provides a dedicated PostgreSQL OpenIddict context.

The authorization endpoint authenticates the existing Hello Razor cookie and
validates its Identity logical session before issuing a code. Code redemption
validates that source again and creates a distinct transport-neutral Identity
session for the client. The private source `sid` exists only inside the
protected authorization code. Issued access and refresh principals carry the
new logical `sid`; refresh and the composed bearer authentication handler
validate it online. OpenIddict owns protocol token rotation and storage while
Identity remains the common revocation boundary for JWT, Razor and OAuth
sessions.

The facade's `IHelloAccessTokenValidator<TProfile>` chain lets ordinary account
operations accept either Identity JWTs or OpenIddict reference access tokens
without moving token-format knowledge into endpoint handlers. A composite
default bearer scheme selects the correct maintained handler. OAuth session
validation occurs in authentication itself, so named admin policies get the
same immediate revocation semantics.

Skopka.Identity owns:

- user/profile, credentials and normalized handles;
- security stamps and optimistic concurrency;
- refresh chains and JWT/refresh token providers;
- versioned persistent account/client rate limiting;
- verification challenges/proofs, OTP HMAC key ids and one-time consumption;
- step-up policy enforcement and password credential mutation;
- persistence entities, PostgreSQL mappings and migrations.

Skopka.Hello owns:

- HTTP DTOs, route authorization and `ProblemDetails`;
- shared application operation results;
- refresh, UI authentication and antiforgery cookies;
- trusted request-derived client/session display context;
- server configuration and migration composition;
- trusted client partition derivation and scheduled bounded pruning;
- account-message link construction and delivery orchestration;
- password-change action/binding derivation and OTP message delivery;
- trusted external-provider composition, pending browser tickets and callback routing;
- OpenIddict protocol composition and client/session binding;
- external link/unlink step-up binding and session replacement;
- security-event request enrichment and audit-outbox contracts.

## Errors

All expected identity failures remain
`Skopka.Abstraction.OperationResult.OperationResult`. The endpoint mapper uses
the stable error type and code:

| Error type/outcome | HTTP |
| --- | --- |
| Validation | `400` |
| Unauthorized | `401` |
| Forbidden | `403` |
| Not found | `404` |
| Conflict | `409` |
| `identity.rate_limit.exceeded` | `429` plus `Retry-After` |
| `hello.delivery.queue_full` | `429` plus `Retry-After` when available |
| `hello.delivery.not_configured`, `hello.delivery.failed` | `503` |

Arbitrary error details are not serialized. Validation fields and the safe
rate-limit retry timestamp are handled explicitly.

Razor forms map the same structured errors to field or summary validation.
They do not convert expected failures into exceptions.

## Browser session

The Razor UI has its own ASP.NET Core cookie authentication scheme and policy.
The encrypted authentication ticket stores the short-lived access token; the
refresh token remains only in the separate `HttpOnly` refresh cookie. On every
protected UI request, the cookie event validates the access token online with
Skopka.Identity. When it has expired, the handler rotates the refresh session,
replaces both protected tickets and rebuilds safe display claims.

Every Razor mutation uses antiforgery. Minimal API bearer authentication remains
separate and still returns access tokens as JSON.

OIDC correlation and nonce cookies are always `Secure`. Short-lived external
and pending tickets are encrypted cookies and contain no provider tokens. The
external completion, registration and sign-in-method pages use no-store and
no-referrer response headers.

## Security events and outbox boundary

`HelloIdentitySecurityEventObserver` enriches committed Skopka.Identity events
with actor and correlation context and sends them to
`IHelloSecurityEventSink`. The default sink is a no-op and this callback is not
a durable audit.

`HelloAuditOutboxRecord` and `IHelloAuditOutbox` define the host-owned durable
shape: event type, subject, actor, resource, correlation id, timestamp and safe
metadata. A post-commit identity observer cannot make an outbox write atomic
with an already committed identity mutation. A consuming application that
requires that guarantee must write the outbox record inside the transaction
that owns its protected application operation. No cross-store atomicity is
claimed.

The ready Server persists these post-commit observer records in
`skopka_hello.audit_outbox`. A failed audit insert is logged and metered but
cannot roll back the already committed Identity mutation. Application
operations that require atomic domain mutation plus audit still need to call
`IHelloAuditOutbox` inside their own transaction boundary.

## Deliberate protocol limits

The external-provider client uses the maintained ASP.NET Core OpenID Connect
handler and the optional authorization server uses OpenIddict. The current
first-party server does not include third-party consent, dynamic registration,
device flow, legacy password/client-credentials grants, user-info, logout or
introspection endpoints. Role administration uses Identity's bounded query and
application services rather than bypassing them with direct store access.
