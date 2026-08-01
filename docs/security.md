# Security Model

This document describes the current transport and deployment controls. It is
not a substitute for an application threat model or independent review.

## Password login

Clients choose `email` or `userName`; Skopka.Identity performs normalization and
password verification. Unknown users, missing credentials and wrong passwords
all become the same public `401` response. Passwords are never logged or copied
into profile data.

The client/rate-limit key is created from the server connection address through
`IHelloRequestContext`; request bodies cannot supply it. Configure forwarded
headers only with explicit trusted proxies; the ready server refuses forwarded
headers without a configured proxy IP. Session metadata contains bounded
client and user-agent display labels and never the remote IP address.

## Access and refresh tokens

Access tokens are returned in JSON and sent with the `Bearer` scheme. Refresh
tokens are returned only in the `__Host-Skopka.Hello.Refresh` cookie with:

- `Secure`;
- `HttpOnly`;
- `SameSite=Strict` by default;
- `Path=/`;
- no `Domain`.

Refresh rotation is one-time and reuse handling is owned by Skopka.Identity.
Plain refresh tokens are not persisted by Skopka.Identity.

The default bearer mode is stateless until JWT expiry. Set
`SkopkaHello:Jwt:ValidateSessionOnEveryRequest=true` when every request must
check the current persisted session and security stamp. This adds a database
lookup. Role and user changes otherwise affect newly issued access tokens only.

The Razor UI uses a separate cookie authentication scheme. Its encrypted,
`HttpOnly` authentication ticket stores the short-lived access token, while the
plain refresh token remains only in the refresh cookie. Every protected Razor
request validates the access token online. When it expires, the cookie handler
uses strict refresh rotation and renews both protected browser artifacts. A
revoked session therefore stops authorizing the UI immediately.

## CSRF

`POST /auth/refresh` and `POST /auth/logout` validate ASP.NET Core antiforgery
tokens because their authority comes from an automatically sent cookie. Login
sets:

- an `HttpOnly` antiforgery framework cookie;
- a readable `__Host-Skopka.Hello.XSRF-TOKEN` request-token cookie.

The client copies the request-token value into `X-CSRF-TOKEN`. The readable token
is not a credential; the refresh token remains inaccessible to JavaScript.
Bearer-authorized account mutations do not derive authority from cookies.
Razor form mutations use the framework antiforgery hidden field and cookie.
Login and registration are also antiforgery protected.

## External OIDC providers

External sign-in uses `Microsoft.AspNetCore.Authentication.OpenIdConnect`, not
custom protocol code. Each enabled provider has a fixed configured authority,
client id, client secret, scope set and stable provider id. The handler uses the
authorization-code flow with PKCE and validates state, correlation, nonce,
issuer, audience, lifetime and signing keys. Dynamic authorities, callback
paths and request-selected scopes are not accepted.

The ready server constructs `redirect_uri` from the trusted configured
`SkopkaHello:PublicOrigin`, never from an untrusted request `Host` header. The
provider callback is:

```text
{PublicOrigin}/signin-skopka-oidc/{normalized-provider-id}
```

Provider ids are stable application identifiers, not issuer URLs. Changing an
id creates a different Skopka.Identity external-login key. Provider subjects
remain exact and case-sensitive. The mapping from a production provider id to
its authority, tenant and client trust boundary is immutable: never point an
existing id at a different issuer or tenant. Introduce a new provider id and an
explicit account relink or migration instead. A `sub` value is unique only
inside the issuer that produced it.

The OIDC handler does not save provider access, refresh or ID tokens. After
validation it retains only the provider id, `sub` and bounded optional name,
locale and email hints in a short-lived encrypted cookie. The email hint is
used only when the provider supplied exactly one `email_verified=true` claim;
it remains unconfirmed in Skopka.Identity. An equal email address never
auto-links accounts. A user who already owns that address must authenticate the
existing account and link explicitly.

The cross-site protocol callback only creates the validated temporary ticket
and redirects to `/hello/external/complete`. A separate same-origin,
antiforgery-protected POST completes sign-in or registration. This preserves
the default `SameSite=Strict` local-session policy and prevents a callback GET
from directly performing an account mutation. Callback, completion and pending
registration responses are no-store/no-referrer. Do not log callback query
strings, provider error descriptions or external claims.

Every terminal external or pending POST first consumes a random flow id from
the encrypted ticket through `IHelloOidcFlowStore`. A copied ticket therefore
cannot issue another local session or repeat an account mutation. Retryable
form or OTP failures rotate the browser to a new id while preserving the
original absolute expiry; the old id remains consumed. When Identity's HMAC
rate limiter is configured, the default guard is atomic and persistent in its
shared bucket store. Otherwise it uses a bounded, fail-closed process-local
fallback. Multi-replica hosts must use the persistent limiter or replace the
flow store with an atomic shared implementation.

Linking and unlinking require the current UI session to pass online access-token
validation and require a confirmed local email. The provider/subject pair,
local user, logical session and optimistic version are bound into a protected
pending ticket. Identity issues and consumes a one-time email code bound to the
exact link or unlink action. A step-up decision is never carried across the
provider redirect.

Before unlink, Hello reads the current sign-in-method snapshot and refuses to
remove the final enabled method. The snapshot version is used for the mutation;
a concurrent account change fails instead of being retried after the OTP was
consumed. Link and unlink rotate the security stamp. Hello then revokes all
existing refresh sessions and issues a new session only to the current browser.
Any failure after the OTP proof has been consumed is terminal and is surfaced
as `hello.account.external_mutation_restart_required`, never as a retryable code
form. The Razor UI clears the pending flow, refresh/antiforgery cookies and its
local authentication ticket, then requires a fresh sign-in so the user reviews
the authoritative current account state.
Stateless bearer access tokens can still validate cryptographically until their
short expiry; enable online bearer validation when the stamp change must take
effect on every API request immediately.

OIDC correlation and nonce cookies are always `Secure` and `SameSite=None` as
required for the cross-site protocol response. Therefore external providers
are disabled in the plain HTTP launch profile and development compose stack.
Do not weaken these cookies for local testing; use the HTTPS launch profile or
a correctly configured TLS reverse proxy.

## Account messages and action tokens

Password-reset and email-confirmation request endpoints return the same
`202 Accepted` response for every well-formed address. Exact normalized lookup
is performed through Skopka.Identity, but a not-found result is suppressed at
the public boundary. Rate-limit these endpoints at the deployment edge.

The built-in SMTP implementation places messages in a bounded background queue,
so SMTP network latency is not exposed in the anonymous request. This queue is
best-effort and in-memory. Applications requiring durable delivery should
replace `IHelloAccountMessageSender` with a durable queue producer.

Action links are built only from configured `SkopkaHello:PublicOrigin`; request
host headers are not trusted. Token pages set `Cache-Control: no-store`,
`Referrer-Policy: no-referrer` and `X-Robots-Tag: noindex, nofollow`.
Email confirmation is a POST mutation, preventing link-preview and mail-scanner
GET requests from confirming an address.

Action tokens are purpose-, user-, target-, security-stamp- and expiry-bound by
Skopka.Identity. Tokens, recipient addresses and passwords are not logged.

## Authenticated password change and step-up

Changing a password requires a confirmed email and an OTP challenge issued by
Skopka.Identity. The application validates the bearer or protected UI access
token online and derives the user id, action and binding itself. None of these
values or the user's optimistic-concurrency version are accepted from the
request.

The HTTP response contains only the challenge id and expiry. The OTP is passed
directly to `IHelloAccountMessageSender`, is HMAC-protected at rest and is never
logged or serialized to the client. Identity rate-limits challenge issuance
and attempts, binds the proof to the password-change action and user, and
consumes it once before the credential mutation.

After a successful change Identity rotates the security stamp and Hello revokes
all refresh sessions. The UI clears both local cookies. Because bearer mode can
otherwise be stateless, enable
`SkopkaHello:Jwt:ValidateSessionOnEveryRequest=true` when every already-issued
API access token must stop authorizing ordinary protected endpoints
immediately.

## Secrets

Keep these values outside source control:

- JWT signing key, at least 32 random bytes and Base64 encoded for configuration;
- rate-limit HMAC keys, at least 32 random bytes per version;
- verification-code HMAC keys, at least 32 random bytes per version;
- PostgreSQL credentials;
- persisted ASP.NET Core Data Protection key ring and its protection material;
- SMTP credentials;
- external OIDC client secrets;
- any future password pepper.

Do not reuse keys between purposes. Multiple replicas must share the JWT key and
Data Protection key ring, and must overlap rate-limit and verification key
versions during rotation. Configure
`SkopkaHello:DataProtection:KeyPath` to a protected persistent location.

## Logging and responses

Never log passwords, JWTs, refresh tokens, action tokens, OTPs, provider tokens
or raw request bodies on authentication routes. Problem responses expose stable
codes, safe validation fields and trace ids, not EF exceptions or arbitrary
error details.

## Deployment checklist

- Terminate TLS at the application or a trusted proxy.
- Leave secure cookies enabled in production.
- Restrict trusted proxy configuration.
- Apply migrations as a controlled deployment step; do not let every replica
  race to migrate.
- Keep the bounded session and rate-limit pruning workers running on at least
  one replica.
- Persist and protect Data Protection keys.
- Choose deliberately between stateless and online bearer validation.
- Keep persistent account/client rate limiting enabled before exposing login
  publicly.
- Keep external providers disabled until their HTTPS authority, exact callback
  URL and client credentials are configured.
- Share provider configuration and Data Protection keys across replicas.
- Exclude OIDC callback query strings and provider tokens from proxy and
  application logs.
- Keep access-token lifetimes short.
- Configure a trusted public origin and rate-limit anonymous account-message
  requests.
- Use a durable delivery queue when message loss during restart is unacceptable.
- Scan images and dependencies and perform an application-specific review.
