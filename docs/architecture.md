# Architecture

Skopka.Hello is the ASP.NET Core layer above Skopka.Identity. It does not fork or
reimplement the identity domain.

## Dependency direction

```text
Server / Sample
  -> Skopka.Hello.Endpoints
  -> Skopka.Hello.UI
  -> Skopka.Hello.Oidc

Endpoints / UI
  -> Skopka.Hello.Oidc

Endpoints / UI / Oidc
  -> Skopka.Hello

Skopka.Hello
  -> Skopka.Identity packages

Admin
  -> Skopka.Hello
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
    .UseJwtSessions(signingKey, jwt =>
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

`AddSkopkaHello<TProfile>` validates the UI prefix and registers one immutable
`HelloUiRoutePaths` snapshot. Core action links, Razor route conventions and
OIDC browser redirects consume that same snapshot, so hosts cannot configure
different paths for the three layers. Disabling self-registration gates both
password and external application operations before Identity is called and
removes their public Minimal API/Razor selectors. Administrative registration
is outside this self-service policy.

Password-reset and email/phone-confirmation requests validate and normalize the
target, consume persistent client and target rate-limit partitions, and enter
one bounded in-memory queue before any account lookup or action-token work.
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
channel; invalid routing fails at startup. The SMTP email provider has its own
bounded background queue, while custom hosts can register an SMS provider
without moving delivery into Identity.
Applications can replace the sender with a durable delivery producer.
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

It redirects to `{UiPathPrefix}/external/complete`. That page performs a separate
antiforgery-protected POST before resolving the external identity, registering
an account or retaining a pending link. This two-stage flow also ensures the
strict same-site local UI cookie is available again after the cross-site
provider callback.

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

## Deferred modules

The external-provider client is implemented with the maintained ASP.NET Core
OpenID Connect handler. An OAuth/OIDC authorization server and administration
API/UI remain deferred; `Skopka.Hello.Admin` is only a package boundary and the
project does not implement a home-grown authorization protocol.
