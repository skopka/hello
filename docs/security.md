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

## CSRF

`POST /auth/refresh` and `POST /auth/logout` validate ASP.NET Core antiforgery
tokens because their authority comes from an automatically sent cookie. Login
sets:

- an `HttpOnly` antiforgery framework cookie;
- a readable `__Host-Skopka.Hello.XSRF-TOKEN` request-token cookie.

The client copies the request-token value into `X-CSRF-TOKEN`. The readable token
is not a credential; the refresh token remains inaccessible to JavaScript.
Bearer-authorized account mutations do not derive authority from cookies.

## Secrets

Keep these values outside source control:

- JWT signing key, at least 32 random bytes and Base64 encoded for configuration;
- PostgreSQL credentials;
- persisted ASP.NET Core Data Protection key ring and its protection material;
- any future password pepper, OTP HMAC key or rate-limit partition key.

Do not reuse keys between purposes. Multiple replicas must share the JWT key and
Data Protection key ring. Configure
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
- Keep the bounded session-pruning worker running on at least one replica.
- Persist and protect Data Protection keys.
- Choose deliberately between stateless and online bearer validation.
- Add persistent rate limiting before exposing login publicly.
- Keep access-token lifetimes short.
- Scan images and dependencies and perform an application-specific review.
