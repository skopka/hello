# Server Module Instructions

Read `../../AGENTS.md` first.

This executable composes PostgreSQL, the selected password hasher, JWT sessions,
versioned persistent rate limiting, versioned OTP HMAC verification, bearer
validation, endpoints, health checks and Docker behavior. Keep secrets outside
source and appsettings. Migrations run only through the one-shot `--migrate`
command and must complete before production replicas start.

The ready composition uses PostgreSQL for the anonymous account-message inbox,
email outbox and post-commit audit outbox. Protect every sensitive delivery
payload with Data Protection before persistence, keep all replicas on the same
key ring, and never log decrypted values. Workers use bounded leases with
`FOR UPDATE SKIP LOCKED`, retry safely and retain failed records only for the
configured interval. SMTP runs synchronously behind the durable outbox so an
outbox row is acknowledged only after the provider returns success.
Production deployments protect the persisted Data Protection key ring with a
PFX supplied from a secret mount; never add that certificate or password to
source or the image.

When the optional authorization server uses self-contained access tokens, keep
its access lifetime short and load an asymmetric signing PFX so discovery/JWKS
can support offline resource servers. The separate encryption PFX remains
required for other protocol artifacts. Reference access tokens remain the
configuration default; authorization codes and refresh tokens remain reference
tokens in both modes.

Rate-limit HMAC keys use a current version and optional overlapping historical
versions. All replicas must share the configured key material during overlap.
Keep both session and rate-limit bounded pruning workers registered. The
checked-in Development configuration may contain clearly public test-only keys
for clone-and-run, but it must stay excluded from publish and Docker output.
Production key material always stays outside source and appsettings.

OTP HMAC keys use an independent current version and overlapping historical
versions. Never reuse JWT or rate-limit keys for verification.

External OIDC client ids and secrets come from configuration. Keep providers
disabled in checked-in settings. Callback origins come from the trusted
`SkopkaHello:PublicOrigin`, all replicas share Data Protection keys, and the
plain HTTP profile must not weaken Secure OIDC correlation or nonce cookies.

Action-token links require configured `SkopkaHello:PublicOrigin`. SMTP remains
optional and credentials come from secrets/environment configuration; omitting
the SMTP host leaves the built-in provider unregistered. Empty channel provider
ids keep delivery disabled without installing a null sender.

The Server contains host configuration, not reusable identity or endpoint
business logic. OpenAPI generation and Swagger UI stay Development-only by
default; do not expose the API contract in production through a checked-in
configuration switch.

Read `SkopkaHello:SelfRegistration:Enabled` and
`SkopkaHello:Ui:PathPrefix` before `AddSkopkaHello<TProfile>` and pass both to
that single DI options callback. The UI prefix moves only Hello Razor routes;
never implement it with `UsePathBase`, which would also move APIs, health and
the provider callback.
