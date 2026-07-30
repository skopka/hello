# Architecture

Skopka.Hello is the ASP.NET Core layer above Skopka.Identity. It does not fork or
reimplement the identity domain.

## Dependency direction

```text
Server / Sample
  -> Skopka.Hello.Endpoints
  -> Skopka.Hello.UI
  -> Skopka.Hello
  -> Skopka.Identity packages

Oidc / Admin
  -> Skopka.Hello
```

`Skopka.Hello` calls `AddSkopkaIdentity<TProfile>()` and returns its
`IdentityBuilder<TProfile>`. The host then selects PostgreSQL, a password hasher,
JWT sessions and bearer validation explicitly:

```csharp
var identity = services
    .AddSkopkaHello<MyProfile>()
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
```

## First vertical flow

`IHelloIdentityApplication<TProfile>` is the shared transport-facing
orchestrator. Registration maps to
`RegisterPasswordUserCommand<TProfile>` and uses the atomic registration
service. Login authenticates one explicit `PasswordLoginHandle`, then passes
the returned user id and current security stamp to session creation. Refresh
delegates strict rotation to Skopka.Identity. Minimal API and Razor handlers
call the same operations and never call EF stores directly.

Password-reset and email-confirmation requests use the exact normalized
`IIdentityUserLookupService<TProfile>` contract. The application suppresses
not-found and delivery outcomes at the anonymous boundary, issues a
purpose-bound Identity action token, builds the link from configured
`PublicOrigin` and hands the message to `IHelloAccountMessageSender`. The
built-in SMTP adapter enqueues to a bounded background worker; applications can
replace it with a durable delivery producer.

Authenticated password change validates the access token online, derives the
user id, optimistic-concurrency version, action and binding on the server, and
uses Skopka.Identity step-up verification before calling
`IPasswordCredentialService<TProfile>.ChangePasswordAsync`. API and Razor UI
share this operation. The transport receives only a safe challenge id and
expiry; the OTP is sent through `IHelloAccountMessageSender`. A successful
change revokes all sessions after Identity rotates the security stamp.

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

Remaining credential lifecycle, external-login management and admin pages
remain deferred. `Skopka.Hello.Oidc` and `Skopka.Hello.Admin` are real package
boundaries, but contain no speculative protocol or identity logic. OAuth/OIDC
will use a maintained protocol library only after target-framework support and
threat modeling are verified.
