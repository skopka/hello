# Deployment

## Container

Build from the repository root so the local package feed is in the Docker
context:

```powershell
docker build -f .\src\Skopka.Hello.Server\Dockerfile -t skopka-hello:local .
```

The runtime image runs as the non-root .NET image user and includes a
process-based health check for `/health/live`.

The image declares `/var/lib/skopka-hello/customization` as a volume and reads
`custom.css` from it by default. Mount this directory read-only. Only the
configured file is served; directory browsing and arbitrary request-to-file
mapping are not enabled.

The account UI is served under `/hello`. The packaged stylesheet is an RCL
static asset; the mounted custom stylesheet is linked after it.

## Configuration

Inject the PostgreSQL connection string, Base64 JWT signing key and versioned
rate-limit HMAC keys from a secret manager. Do not bake `.env`, database
passwords or signing material into the image. Use a protected persistent volume
for Data Protection keys.

Set `SkopkaHello:PublicOrigin` to the public TLS origin used in account-message
links. Configure SMTP credentials from a secret manager. The built-in SMTP
worker uses an in-memory bounded queue; replace
`IHelloAccountMessageSender` with a durable broker producer when restart-safe
delivery is required.

Expose the app through TLS. If TLS terminates at a reverse proxy, configure
ASP.NET Core forwarded headers with explicit known proxies/networks; otherwise
remote address-derived rate-limit context is untrusted.

For a single trusted proxy:

```text
SkopkaHello__ForwardedHeaders__Enabled=true
SkopkaHello__ForwardedHeaders__KnownProxies__0=10.0.0.10
```

The server accepts one forwarded hop and refuses to enable this mode without an
explicit proxy IP.

The development compose stack publishes plain HTTP and therefore disables secure
cookies and uses non-`__Host-` names. Do not copy that override into production.

## Database migrations

Skopka.Identity PostgreSQL migrations are packaged in
`Skopka.Identity.Ef.PostgreSql`. Apply them once as a controlled deployment
step. The server's `SkopkaHello:Database:ApplyMigrations` switch is convenient
for the single-replica compose stack but should normally be false on replicated
production workloads.

Back up the PostgreSQL database and Data Protection key ring and test restoring
them together.

## Scaling and revocation

Every replica must share:

- PostgreSQL;
- JWT signing configuration;
- Data Protection keys;
- the same current and overlapping historical rate-limit key versions;
- future verification keys.

To rotate a rate-limit key, deploy the new key as another entry under
`SkopkaHello:RateLimiting:Keys`, set `CurrentVersion` to its version and retain
the previous entry. After every old-only replica has stopped, wait at least the
longest active rate-limit window before removing the previous key. A deployment
without an overlapping version cannot preserve active counters.

```text
SkopkaHello__RateLimiting__CurrentVersion=v2
SkopkaHello__RateLimiting__Keys__v1=<previous Base64 key>
SkopkaHello__RateLimiting__Keys__v2=<new Base64 key>
```

The default JWT check is stateless. A revoked refresh session cannot mint new
access tokens, but an already issued access token remains valid until expiry.
Enable online validation when immediate revocation is more important than the
extra database read.

## Operational checks

- poll `/health/live` for process liveness;
- poll `/health/ready` for PostgreSQL connectivity;
- monitor the hourly bounded refresh-session pruning worker;
- monitor the hourly bounded rate-limit bucket pruning worker;
- monitor authentication failures and rate-limit decisions without submitted
  secrets;
- monitor background account-message failures by safe error code;
- keep container base images and NuGet dependencies patched;
- run Release build, unit tests, Testcontainers integration tests and Docker
  build for each release.
