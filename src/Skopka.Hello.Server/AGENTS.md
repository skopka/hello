# Server Module Instructions

Read `../../AGENTS.md` first.

This executable composes PostgreSQL, the selected password hasher, JWT sessions,
versioned persistent rate limiting, versioned OTP HMAC verification, bearer
validation, endpoints, health checks and Docker behavior. Keep secrets outside
source and appsettings. Migrations run only behind the explicit configuration
switch and must not race across production replicas.

Rate-limit HMAC keys use a current version and optional overlapping historical
versions. All replicas must share the configured key material during overlap.
Keep both session and rate-limit bounded pruning workers registered.

OTP HMAC keys use an independent current version and overlapping historical
versions. Never reuse JWT or rate-limit keys for verification.

External OIDC client ids and secrets come from configuration. Keep providers
disabled in checked-in settings. Callback origins come from the trusted
`SkopkaHello:PublicOrigin`, all replicas share Data Protection keys, and the
plain HTTP profile must not weaken Secure OIDC correlation or nonce cookies.

Action-token links require configured `SkopkaHello:PublicOrigin`. SMTP remains
optional and credentials come from secrets/environment configuration; omitting
the SMTP host keeps the null delivery adapter.

The Server contains host configuration, not reusable identity or endpoint
business logic.
