# Deployment

## Container

Build from the repository root so the local package feed is in the Docker
context:

```powershell
docker build -f .\src\Skopka.Hello.Server\Dockerfile -t skopka-hello:local .
```

Coordinated releases publish the same server build as
`ghcr.io/skopka/hello:<version>`. Stable releases also update `latest`; pin the
exact version or digest in production rather than following `latest`.

The runtime image runs as the non-root .NET image user and includes a
process-based health check for `/health/live`.

The image declares `/var/lib/skopka-hello/customization` as a volume and reads
`custom.css` from it by default. Mount this directory read-only. Only the
configured file is served; directory browsing and arbitrary request-to-file
mapping are not enabled.

The account UI is served under the startup-configured
`SkopkaHello:Ui:PathPrefix`, `/hello` by default. The packaged stylesheet is an
RCL static asset; the mounted custom stylesheet is linked after it.

The admin API prefix is configured by
`SkopkaHello:Admin:ApiPathPrefix` (`/admin` by default). Its Razor user page is
served under the configured Hello UI prefix plus that value, for example
`/hello/admin/users`; the role page is `/hello/admin/roles`. Read, manage and
delete policy/role names are configured under the same section. Provision the
first administrator only with the
explicit command documented in [administration](administration.md); the normal
web-server startup never seeds a privileged user.

## Configuration

Inject the PostgreSQL connection string plus independent versioned JWT,
rate-limit and verification-code HMAC keys from a secret manager. Do
not bake `.env`, database passwords or signing material into the image. Use a
protected persistent volume for Data Protection keys.

The volume prevents key loss but does not encrypt the XML key ring by itself.
For production, mount a PFX outside the image and set
`SkopkaHello:DataProtection:CertificatePath` plus the secret
`CertificatePassword`. The certificate must contain its private key. All
replicas need the same current certificate while they share that ring. During
rotation, configure prior PFX files under
`DataProtection:DecryptionCertificates:{index}:Path` and `Password`, deploy the
new current certificate with the old certificates still readable, and remove
an old certificate only after no retained key-ring entry or protected queue
payload requires it.

```text
SkopkaHello__DataProtection__CertificatePath=/run/secrets/dp-current.pfx
SkopkaHello__DataProtection__CertificatePassword=<secret>
SkopkaHello__DataProtection__DecryptionCertificates__0__Path=/run/secrets/dp-previous.pfx
SkopkaHello__DataProtection__DecryptionCertificates__0__Password=<secret>
```

The base image does not include Kerberos. Set `GSS Encryption Mode=Disable` in
the PostgreSQL connection string when GSSAPI is not part of the deployment;
otherwise derive an image that installs the required GSSAPI runtime libraries.
Configure PostgreSQL TLS independently through Npgsql `SSL Mode` according to
the deployment trust boundary.

Set `SkopkaHello:PublicOrigin` to the public TLS origin used in account-message
links and external OIDC callback URLs. The ready server passes this trusted
origin to the OIDC adapter, so provider redirects are not derived from the
request `Host` header. Configure SMTP credentials from a secret manager. The
ready Server persists anonymous requests and email messages in PostgreSQL
before processing them; SMTP runs directly behind the durable outbox worker.
Configure both
`SkopkaHello:Delivery:EmailProviderId` and the matching
`SkopkaHello:Delivery:Smtp:ProviderId`; provider routing is validated at
startup. The compose file maps the `SKOPKA_HELLO_EMAIL_PROVIDER_ID` and
`SKOPKA_HELLO_SMTP_*` wrapper variables explicitly. Leave the provider id and
host empty to disable delivery. The ready image contains no SMS vendor adapter;
a derived host image can register one through
`AddSkopkaHelloSmsProvider<TProvider>()` and select its id with
`SkopkaHello:Delivery:SmsProviderId`.
`SkopkaHello:Delivery:AnonymousRequestQueueCapacity` bounds the in-memory
fallback for requests awaiting account lookup and token issuance (the compose wrapper is
`SKOPKA_HELLO_ANONYMOUS_MESSAGE_QUEUE_CAPACITY`). Size it independently from
the SMTP provider queue. Anonymous client and normalized-target admission uses
the persistent Identity rate limiter configured by the ready Server. Denied or
capacity-exhausted anonymous requests are silently dropped after validation so
the enumeration-safe `202 Accepted` contract does not change.
Set `SkopkaHello:Delivery:VerificationChannel` (or the compose wrapper
`SKOPKA_HELLO_VERIFICATION_CHANNEL`) to `Email` or `Sms` for password and
external-account step-up codes. The chosen contact must be confirmed; there is
no cross-channel fallback after challenge creation.

Durable payloads contain protected normalized targets, recipients, action URLs
or OTPs. All replicas must share the Data Protection key ring, and old keys
must remain available until no retained queue record depends on them. Provider
delivery is at-least-once; use the stable `MessageId` as an idempotency key when
the provider supports deduplication. Configure leases, retries and retention
under `SkopkaHello:Persistence`; durable delivery and post-commit audit are
enabled by default. The command timeout bounds synchronous post-commit audit
writes so a database fault cannot hold an identity request indefinitely. The
Compose wrapper exposes the enable flags, maximum-attempt value and both
retention intervals shown in `.env.example`.

```text
SkopkaHello__Persistence__DurableDeliveryEnabled=true
SkopkaHello__Persistence__AuditEnabled=true
SkopkaHello__Persistence__CommandTimeout=00:00:05
SkopkaHello__Persistence__LeaseDuration=00:01:00
SkopkaHello__Persistence__RetryDelay=00:00:10
SkopkaHello__Persistence__MaximumAttempts=8
SkopkaHello__Persistence__AnonymousRequestLifetime=01:00:00
SkopkaHello__Persistence__FailedRecordRetention=7.00:00:00
SkopkaHello__Persistence__AuditRetention=90.00:00:00
```

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

Configure registration exposure and the UI prefix with:

```text
SkopkaHello__SelfRegistration__Enabled=true
SkopkaHello__Ui__PathPrefix=/hello
```

The compose wrapper exposes the equivalent
`SKOPKA_HELLO_SELF_REGISTRATION_ENABLED` and
`SKOPKA_HELLO_UI_PATH_PREFIX` variables. Both values are read while services
are registered and require a restart to change. Disabling self-registration
removes the built-in password API route and both password/external registration
pages while preserving existing sign-in and account linking.

The UI prefix is not a reverse-proxy application `PathBase`: it moves only the
Razor pages, their internal redirects and account-message action paths. The API,
health checks, static assets and `/signin-skopka-oidc/{provider}` callback stay
root-relative. `SkopkaHello:PublicOrigin` remains an origin without a path.
The prefix must be a non-empty absolute path other than `/`. Root and prefixes
in those reserved namespaces are rejected during service registration rather
than producing ambiguous endpoints at request time.

The development compose stack publishes plain HTTP and therefore disables secure
cookies and uses non-`__Host-` names. Do not copy that override into production.
External OIDC providers are also disabled in the checked-in configuration.
Correlation and nonce cookies remain `Secure`, so provider login must be tested
through the HTTPS launch profile or a TLS reverse proxy rather than by weakening
the protocol cookies.

## External OIDC provider configuration

The ready Server binds providers from
`SkopkaHello:ExternalOidc:Providers`. The checked-in Google entry is an inert
example: `Enabled` is false and both credential fields are empty. Supply the
client credentials from a secret manager and enable all values together:

```text
SkopkaHello__ExternalOidc__Providers__google__Enabled=true
SkopkaHello__ExternalOidc__Providers__google__ClientId=<provider client id>
SkopkaHello__ExternalOidc__Providers__google__ClientSecret=<provider client secret>
```

The non-secret schema for each provider is:

```json
{
  "Enabled": false,
  "DisplayName": "Google",
  "Authority": "https://accounts.google.com",
  "ClientId": "",
  "ClientSecret": "",
  "RequireHttpsMetadata": true,
  "Order": 10,
  "Scopes": []
}
```

`Scopes` adds provider-specific scopes to the built-in `openid`, `profile` and
`email` set. Keep metadata HTTPS validation enabled. Provider ids are the keys
under `Providers`; they are normalized to lower case, must remain stable and
must contain 1-64 ASCII letters, digits, `.`, `_` or `-`. Do not rename an id
after accounts have linked it unless the deployment intentionally treats it as
a new provider. Likewise, never reuse an existing id for another authority,
tenant or client trust boundary: create a new id and perform an explicit relink
or migration. Skopka.Identity persists the provider id and exact subject, so a
silent authority swap could otherwise reinterpret an issuer-local `sub`.

The remaining `SkopkaHello:ExternalOidc` settings are:

```text
PasswordSignInEnabled     true
ExternalCookieName        __Host-Skopka.Hello.External
ExternalCookieLifetime    00:05:00
PendingCookieName         __Host-Skopka.Hello.External.Pending
PendingCookieLifetime     00:10:00
```

Both lifetimes must be between one and thirty minutes. Keep the `__Host-`
cookie names for HTTPS deployments. The ready Server takes `PublicOrigin` and
the secure-cookie mode from the corresponding global `SkopkaHello` settings,
so do not configure conflicting OIDC-local values.

Register this exact callback URI in the provider console:

```text
https://public.example/signin-skopka-oidc/google
```

Replace the origin and final segment with the configured public origin and
provider id. `{UiPathPrefix}/external/complete` is the internal same-origin
completion page and is not registered with the provider.

`SkopkaHello:ExternalOidc:PasswordSignInEnabled` tells the external-login
management policy whether an existing password counts as an alternate method
when unlinking. Keep it aligned with whether the host actually exposes password
login.

## Database migrations

Skopka.Identity PostgreSQL migrations are packaged in
`Skopka.Identity.Ef.PostgreSql`; the ready Server also owns versioned
`skopka_hello` delivery/audit migrations. Apply both once as a controlled
deployment step before starting the new web replicas:

```powershell
docker run --rm `
  -e ConnectionStrings__Identity="Host=..." `
  ghcr.io/skopka/hello:<version> --migrate
```

The command reads only `ConnectionStrings:Identity`, applies and verifies all
pending Identity and Hello migrations and then exits. Hello migration ids and
SHA-256 checksums are recorded in `skopka_hello.schema_migrations`; editing an
applied migration fails instead of silently changing history. The command is
idempotent and takes a PostgreSQL advisory transaction lock, but the deployment
platform should still run one migration job. The web process never applies
migrations during startup. The provided Compose stack models the same ordering
with a one-shot `migrate` service and `service_completed_successfully`
dependency.

Back up the PostgreSQL database and Data Protection key ring and test restoring
them together.

## Scaling and revocation

Every replica must share:

- PostgreSQL;
- JWT signing configuration;
- Data Protection keys;
- the same current and overlapping historical rate-limit key versions;
- the same current and overlapping historical verification key versions;
- identical enabled OIDC provider ids, authorities, clients and scopes.

To rotate a JWT signing key, deploy the new key as another entry under
`SkopkaHello:Jwt:Keys`, set `CurrentVersion` to its id and retain every key
that may have signed an unexpired access token. Identity writes the current id
to the JWT `kid` header and validates tokens against the matching configured
key. Remove the previous key only after the longest access-token lifetime plus
clock skew has elapsed since the last replica using it stopped issuing tokens.

```text
SkopkaHello__Jwt__CurrentVersion=v2
SkopkaHello__Jwt__Keys__v1=<previous Base64 key>
SkopkaHello__Jwt__Keys__v2=<new Base64 key>
```

The default OIDC replay guard uses the configured Identity rate limiter with a
fixed 30-minute one-use bucket. The ready Server therefore gets a shared atomic
guard from PostgreSQL automatically. A host that omits the persistent limiter
uses only the bounded process-local fallback and must not scale external flows
across replicas unless it registers a shared atomic `IHelloOidcFlowStore`.

To rotate a rate-limit key, deploy the new key as another entry under
`SkopkaHello:RateLimiting:Keys`, set `CurrentVersion` to its version and retain
the previous entry. After every old-only replica has stopped, wait at least the
longest active rate-limit window and never less than 30 minutes before removing
the previous key. The 30-minute floor preserves consumed OIDC flow ids. A
deployment without an overlapping version cannot preserve active counters.

```text
SkopkaHello__RateLimiting__CurrentVersion=v2
SkopkaHello__RateLimiting__Keys__v1=<previous Base64 key>
SkopkaHello__RateLimiting__Keys__v2=<new Base64 key>
```

Verification keys use the same overlap pattern under
`SkopkaHello:Verification:Keys`. Retain every key version referenced by an
unexpired verification challenge; removing it earlier invalidates that
challenge. Key ids are stored inside the HMAC verifier in PostgreSQL, while raw
keys remain only in host configuration.

```text
SkopkaHello__Verification__CurrentVersion=v2
SkopkaHello__Verification__Keys__v1=<previous Base64 key>
SkopkaHello__Verification__Keys__v2=<new Base64 key>
```

Never reuse a JWT, rate-limit or verification key for another purpose.

All replicas handling external callbacks must share the Data Protection key
ring because OIDC state, correlation and pending external tickets are protected
browser artifacts. Deploy provider configuration atomically across replicas so
a callback is not routed to an instance that does not recognize its named
provider scheme.

The default JWT check is stateless. A revoked refresh session cannot mint new
access tokens, but an already issued access token remains valid until expiry.
Enable online validation when immediate revocation is more important than the
extra database read.

## Operational checks

- poll `/health/live` for process liveness;
- poll `/health/ready` for PostgreSQL connectivity and current Identity/Hello
  schemas;
- monitor the hourly bounded refresh-session pruning worker;
- monitor the hourly bounded rate-limit bucket pruning worker;
- alert on rate-limit pruning budget exhaustion event `1013`;
- monitor authentication failures and rate-limit decisions without submitted
  secrets;
- monitor background account-message failures by safe error code;
- collect the `Skopka.Hello` and `Skopka.Hello.Server` meters; alert on
  `skopka.hello.account_message.queue.dropped`,
  `skopka.hello.persistence.failure` and
  `skopka.hello.delivery.dead_letter`;
- monitor external sign-in failures by safe error code without callback query
  strings, subjects, claims or provider tokens;
- keep container base images and NuGet dependencies patched;
- run Release build, unit tests, Testcontainers integration tests and Docker
  build for each release.
