# Deployment

## Container

Build from the repository root so the local package feed is in the Docker
context:

```powershell
docker build -f .\src\Skopka.Hello.Server\Dockerfile -t skopka-hello:local .
```

The runtime image runs as the non-root .NET image user and includes a
process-based health check for `/health/live`.

## Configuration

Inject the PostgreSQL connection string and Base64 JWT signing key from a secret
manager. Do not bake `.env`, database passwords or signing material into the
image. Use a protected persistent volume for Data Protection keys.

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
- future rate-limit and verification keys.

The default JWT check is stateless. A revoked refresh session cannot mint new
access tokens, but an already issued access token remains valid until expiry.
Enable online validation when immediate revocation is more important than the
extra database read.

## Operational checks

- poll `/health/live` for process liveness;
- poll `/health/ready` for PostgreSQL connectivity;
- monitor the hourly bounded refresh-session pruning worker;
- monitor authentication failures and rate-limit decisions without submitted
  secrets;
- keep container base images and NuGet dependencies patched;
- run Release build, unit tests, Testcontainers integration tests and Docker
  build for each release.
