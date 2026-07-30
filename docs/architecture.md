# Architecture

Skopka.Hello is the ASP.NET Core layer above Skopka.Identity. It does not fork or
reimplement the identity domain.

## Dependency direction

```text
Server / Sample
  -> Skopka.Hello.Endpoints
  -> Skopka.Hello
  -> Skopka.Identity packages

UI / Oidc / Admin
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
    .UseJwtSessions(signingKey, jwt =>
    {
        jwt.Issuer = issuer;
        jwt.Audience = audience;
    });

identity.UseJwtBearerAuthentication();
```

## First vertical flow

Registration maps an HTTP DTO to `RegisterPasswordUserCommand<TProfile>` and
uses the atomic registration service. Login authenticates one explicit
`PasswordLoginHandle`, then passes the returned user id and current security
stamp to session creation. Refresh delegates strict rotation to
Skopka.Identity. Account endpoints use the authenticated subject and never call
EF stores directly.

Skopka.Identity owns:

- user/profile, credentials and normalized handles;
- security stamps and optimistic concurrency;
- refresh chains and JWT/refresh token providers;
- persistence entities, PostgreSQL mappings and migrations.

Skopka.Hello owns:

- HTTP DTOs, route authorization and `ProblemDetails`;
- refresh and antiforgery cookies;
- trusted request-derived client/session display context;
- server configuration and migration composition;
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

`Skopka.Hello.UI`, `Skopka.Hello.Oidc` and `Skopka.Hello.Admin` are real package
boundaries, but contain no speculative protocol or identity logic. OAuth/OIDC
will use a maintained protocol library only after target-framework support and
threat modeling are verified.
