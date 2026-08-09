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
JWTs carry the configured signing-key id in `kid`; validators accept the
current and explicitly retained overlapping keys. Keep an old key until no
unexpired token can reference it.

The Razor UI uses a separate cookie authentication scheme. Its encrypted,
`HttpOnly` authentication ticket stores the short-lived access token, while the
plain refresh token remains only in the refresh cookie. Every protected Razor
request validates the access token online. When it expires, the cookie handler
uses strict refresh rotation and renews both protected browser artifacts. A
revoked session therefore stops authorizing the UI immediately.

### Authorization-server access tokens

The optional OpenIddict server keeps reference access tokens as its default.
`SelfContainedJwt` is an explicit interoperability mode for offline resource
servers: access tokens are asymmetrically signed JWSs, access-token encryption
is disabled, and authorization codes plus refresh tokens remain reference
tokens. Use a short, independently configured access-token lifetime. Signing
and encryption certificates are separate from the Identity JWT HMAC key set.

Each client has one fixed configured resource. The authorization request cannot
select another client's audience, and Hello's local OpenIddict validation
accepts only the configured Hello API resource. The composite bearer selector
may inspect unverified JWT `iss`/`typ` solely to select a candidate handler;
the selected Identity or OpenIddict handler still validates the signature,
exact issuer, audience, lifetime, algorithm and token type. ID tokens are never
accepted as access tokens.

OAuth access and refresh at Hello validate the bound Identity logical `sid`
online. Revocation therefore blocks Hello APIs and refresh immediately. An
external resource server validating a JWT only from discovery/JWKS cannot see
that revoke and may accept the token until `exp`; this is the deliberate
revocation window behind the five-minute mail-token recommendation.

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

The cross-site protocol callback only creates the validated temporary ticket.
The built-in Razor flow redirects to `{UiPathPrefix}/external/complete`; the
headless same-origin browser flow redirects to a strictly validated local
application landing path. In both cases a separate antiforgery-protected POST
completes sign-in or registration. This preserves the default
`SameSite=Strict` local-session policy and prevents a callback GET from directly
performing an account mutation. Callback, completion and pending registration
responses are no-store/no-referrer. Remote failures reach the headless landing
path only as `externalError=true`; provider error text is not forwarded. Do not
log callback query strings, provider error descriptions or external claims.

The Razor completion and pending registration routes are derived from the
immutable UI prefix registered by `AddSkopkaHello<TProfile>`. The headless
browser routes use the fixed `/auth/external/*` API namespace. Their return URL
must begin with a single local `/`, cannot contain a backslash and cannot point
at the OIDC callback or external API namespaces. When self-registration is
disabled, the headless registration routes are not mapped and an unknown
external identity is rejected before a pending registration ticket is created;
stale pending tickets are cleared. Existing linked external identities continue
through the normal sign-in path.

Every terminal external or pending mutation first consumes a random flow id from
the encrypted ticket through `IHelloOidcFlowStore`. A copied ticket therefore
cannot issue another local session or repeat an account mutation. Retryable
form or OTP failures rotate the browser to a new id while preserving the
original absolute expiry; the old id remains consumed. When Identity's HMAC
rate limiter is configured, the default guard is atomic and persistent in its
shared bucket store. Otherwise it uses a bounded, fail-closed process-local
fallback. Multi-replica hosts must use the persistent limiter or replace the
flow store with an atomic shared implementation.

Headless linking starts with a Bearer-authenticated, antiforgery-protected POST.
It writes a separate short-lived HttpOnly `SameSite=Strict` link-request cookie
containing only the configured provider id, validated local return path,
user/session binding and a random flow id. The response exposes only a local
challenge path. Browser navigation atomically consumes the preflight flow before
the OIDC handler is invoked, so a copied preflight cookie cannot start another
provider round trip. The provider callback then uses the ordinary external and
pending tickets; completion also requires the same online-valid Bearer session.

ASP.NET Core antiforgery tokens are principal-bound. A same-origin API client
must call Bearer-authorized `GET /auth/antiforgery` before linking or unlinking
to replace an anonymous login token with one bound to the current principal.
The request token remains in the configured readable cookie and must be echoed
only through the configured header.

Linking and unlinking require the current UI session to pass online access-token
validation and require a confirmed contact for the configured channel. The
provider/subject pair,
local user, logical session and challenge id are bound into a protected pending
ticket. Identity issues and consumes a one-time code bound to the exact link or
unlink action, provider/subject target, configured delivery channel and a
non-reversible fingerprint of the confirmed destination. A step-up decision is
never carried across the provider redirect.

Before unlink, Hello reads the current sign-in-method snapshot and refuses to
remove the final enabled method. Completion reads a fresh snapshot and uses its
version for the mutation, so an unrelated profile edit while the OTP is pending
does not invalidate the code. A race after that fresh read still fails the
Identity optimistic-concurrency check instead of being retried after the OTP
was consumed. Link and unlink rotate the security stamp. Hello then revokes all
existing refresh sessions and issues a new session only to the current browser.
Only a wrong OTP response is retryable. An expired, superseded or locked
challenge and an authorization failure before account mutation return
`hello.account.external_challenge_restart_required`; the Razor UI clears only
the pending flow and keeps the authenticated local session so the user can
start the provider change again. A mutation, revocation or fresh-session
failure after authorization returns
`hello.account.external_mutation_restart_required`. At that point account state
may have changed, so the UI also clears refresh/antiforgery cookies and its
local authentication ticket, then requires a fresh sign-in to review the
authoritative current account state.
Stateless bearer access tokens can still validate cryptographically until their
short expiry; enable online bearer validation when the stamp change must take
effect on every API request immediately.

OIDC correlation and nonce cookies are always `Secure` and `SameSite=None` as
required for the cross-site protocol response. Therefore external providers
are disabled in the plain HTTP launch profile.
Do not weaken these cookies for local testing; use the HTTPS launch profile or
a correctly configured TLS reverse proxy.

## Account messages and action tokens

Password-reset and email/phone-confirmation request endpoints return the same
`202 Accepted` response for every well-formed address or phone. Before lookup,
Hello applies the configured Identity verification client limit to the trusted
server-derived client key and the verification intent limit plus resend
cooldown to the normalized target. The persistent HMAC rate limiter used by the
ready Server makes these partitions atomic across replicas. A denied decision
or full anonymous queue is silently dropped from the caller's perspective; it
does not change the HTTP response. Queue saturation emits safe event `2001`
and increments `skopka.hello.account_message.queue.dropped` from the
`Skopka.Hello` meter without recording the target. Queue admission happens
before exact normalized lookup, token
issuance and provider dispatch, so known and unknown targets share the complete
HTTP path. An edge rate limit remains useful as an additional coarse
protection.

The account-message dispatcher routes only to the provider id configured for
the semantic email or SMS channel. Missing, duplicate and channel-mismatched
providers fail startup validation. An anonymous-request worker performs lookup
and token work in a fresh scope, then the built-in SMTP email provider places
the resulting message in its own bounded background queue. Neither lookup nor
SMTP network latency is exposed in the anonymous response. A successful result
means the request was queued, not delivered. Workers log only safe provider,
message-kind, message-id and error-code metadata. They never log recipients,
action links or verification codes. The reusable package's default inbox and
SMTP queue are best-effort and in-memory. The ready Server replaces both email
stages with PostgreSQL lease-based persistence and protects normalized targets,
recipients, action URLs and OTPs with Data Protection before writing them.
Delivery is at-least-once: a provider success followed by an acknowledgement
failure may produce a duplicate with the same `MessageId`, so custom providers
should use that id for deduplication where supported.

Action links are built only from configured `SkopkaHello:PublicOrigin`; request
host headers are not trusted. Their reset/confirmation path is derived from the
same configured UI prefix used by Razor routing. Token pages set
`Cache-Control: no-store`,
`Referrer-Policy: no-referrer` and `X-Robots-Tag: noindex, nofollow`.
Email and phone confirmation are POST mutations, preventing link-preview and
message-scanner GET requests from confirming a contact. The email-confirmation
landing page automatically submits its antiforgery-protected form with an
external same-origin script; if scripting is unavailable or blocked, the same
form remains available as a visible manual fallback. Before submitting, the
script removes the query string from browser history and the address bar. The
initial GET still reaches the edge with its action token, so reverse-proxy and
request logging must redact confirmation and reset query strings.

Action tokens are purpose-, user-, target-, security-stamp- and expiry-bound by
Skopka.Identity. Tokens, recipient addresses and passwords are not logged.

## Authenticated credentials, deletion and step-up

Changing a password requires a confirmed contact for the configured
`VerificationChannel` and an OTP challenge issued by Skopka.Identity. The
application validates the bearer or protected UI access
token online and derives the user id, action and binding itself. None of these
values or the user's optimistic-concurrency version are accepted from the
request.

The HTTP response contains only the challenge id, expiry and the non-sensitive
delivery channel (`email` or `sms`). The OTP is passed directly to
`IHelloAccountMessageSender`, is HMAC-protected at rest and is never logged or
serialized to the client. Identity rate-limits challenge issuance
and attempts, binds the proof to the password-change action, user, security
stamp, delivery channel and a non-reversible fingerprint of the confirmed
destination, and consumes it once before the credential mutation. An unrelated
profile edit does not invalidate the code; changing the confirmed destination
or security stamp does.

Only `identity.verification.response_invalid` is retryable with the same
challenge. An expired, superseded, locked or concurrently changed challenge,
and every failure after successful verification but before the password is
changed, returns
`hello.account.password_change_restart_required`. API clients must request a
new challenge. The Razor UI clears its challenge id and renders the request-code
state while retaining the underlying password or concurrency error as
additional diagnostic context.

If the password change commits but refresh-session revocation fails, Hello
returns `hello.account.password_change_session_cleanup_required` instead. The
password is already changed: the UI discards its local cookies and asks the user
to sign in with the new password, while API clients must treat the mutation as
completed and reauthenticate rather than retrying it.

After a successful change Identity rotates the security stamp and Hello revokes
all refresh sessions. The UI clears both local cookies. Because bearer mode can
otherwise be stateless, enable
`SkopkaHello:Jwt:ValidateSessionOnEveryRequest=true` when every already-issued
API access token must stop authorizing ordinary protected endpoints
immediately.

Password setup, password removal and account deletion have separate Identity
step-up actions and purpose-bound delivery bindings, so a proof issued for one
action cannot authorize another. Hello checks current sign-in methods before
issuing a password-removal challenge and rechecks them after proof consumption;
the password cannot be removed unless an external method remains. These
operations derive the current optimistic version from the online-validated
session. On success, Hello revokes every session and the Razor UI signs out.
Completion failures after proof consumption require a new challenge; callers
must not replay an account mutation whose outcome may already have committed.
Password registration and password setup require at least one local login
handle. Account self-service refuses to clear the final user name, email or
phone while password sign-in is configured, preventing a valid credential from
becoming unreachable.

## Administrative actions

Admin API and Razor requests pass three independent gates: an authenticated
bearer/UI session, a live role-backed read/manage/delete policy, and an
Identity-owned step-up decision for mutations. The role policy queries current
membership instead of relying only on the role claim embedded when a token was
issued. The application also validates the administrator's access token online
before querying or mutating users.

The host must explicitly project visible `TProfile` fields through
`IHelloAdminProfileProjector<TProfile>`. Raw profiles are never returned by the
admin application contract. Do not project secrets or cross-tenant fields the
current actor cannot inspect.

Mutation proofs include non-reversible bindings for actor, target, action,
expected version, action parameters, delivery channel and the administrator's
confirmed destination. A code for one target or command cannot authorize
another. Self-block and self-delete are denied. Block and soft-delete revoke
the target's refresh sessions; deployments requiring already-issued access
tokens to stop immediately must retain online session validation on protected
resources.

Role listing uses Identity's bounded query contract. Role CRUD and membership
changes require the highest configured admin policy and a separate bound OTP.
Configured policy roles cannot be renamed or deleted, and an actor cannot
remove their own protected membership. Assign/remove revoke the target's
sessions; the Admin policy handler also reads membership live instead of
trusting a stale role claim. Role CRUD produces Hello post-commit security
events with actor and resource ids for the configured audit sink.

The explicit `--bootstrap-admin <user-id>` operator command assigns only the
configured roles to an existing user and revokes that user's sessions. Do not
replace it with an automatic email lookup, checked-in seed user or startup
assignment from untrusted configuration.

## Secrets

Keep these values outside source control:

- JWT signing keys, at least 32 random bytes each, versioned and Base64 encoded
  for configuration;
- rate-limit HMAC keys, at least 32 random bytes per version;
- verification-code HMAC keys, at least 32 random bytes per version;
- PostgreSQL credentials;
- persisted ASP.NET Core Data Protection key ring and its protection material;
- SMTP credentials;
- external OIDC client secrets;
- authorization-server confidential client secrets and stable signing/
  encryption PFX files;
- any future password pepper.

Do not reuse keys between purposes. Multiple replicas must share the current
and overlapping JWT, rate-limit and verification key versions plus the Data
Protection key ring during rotation. Configure
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
- Keep the authorization server disabled until its HTTPS issuer, exact client
  redirects, per-client resources, confidential secrets and stable protocol
  certificates are configured; run `--migrate` after changing the client set.
- Keep externally validated OAuth JWTs short-lived, require the exact audience
  and scopes at the resource server, and test public JWKS rotation.
- Share provider configuration and Data Protection keys across replicas.
- Exclude OIDC callback query strings and provider tokens from proxy and
  application logs.
- Exclude password-reset and contact-confirmation query strings from proxy and
  application logs.
- Keep access-token lifetimes short.
- Configure a trusted public origin and rate-limit anonymous account-message
  requests.
- Keep the ready Server's durable delivery enabled, or replace both inbox and
  sender contracts when message loss during restart is unacceptable.
- Scan images and dependencies and perform an application-specific review.
