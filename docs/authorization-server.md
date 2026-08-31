# OAuth/OIDC authorization server

`Skopka.Hello.AuthorizationServer` is an optional OpenIddict-based authorization
server for trusted, pre-registered native and BFF clients. It is separate from
`Skopka.Hello.Oidc`, which is the client adapter used when Hello signs users in
through an external provider.

The implemented grant is authorization code with mandatory PKCE S256. Public
native clients do not have a secret. Confidential clients require both PKCE and
a secret; `client_secret_post`, as used by Roundcube, is supported. Request
`offline_access` to receive a rotating reference refresh token.

The server exposes OpenID Connect discovery and JWKS plus:

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/connect/authorize` | Browser SSO and authorization-code issuance |
| `POST` | `/connect/token` | Code redemption and refresh-token rotation |
| `GET`, `POST` | `/connect/logout` | Browser SSO termination and client return |

There is no UserInfo or introspection endpoint. External resource servers use a
self-contained access token and validate its signature, issuer, audience,
lifetime, token type and scopes from discovery/JWKS.

## Access-token formats

`HelloAuthorizationServerOptions.AccessTokenFormat` has two values:

- `HelloAuthorizationAccessTokenFormat.Reference` is the compatibility default.
  Access and refresh tokens are opaque OpenIddict reference tokens.
- `HelloAuthorizationAccessTokenFormat.SelfContainedJwt` issues a signed,
  unencrypted JWT access token suitable for offline validation. Authorization
  codes and refresh tokens remain opaque reference tokens.

`AccessTokenLifetime` controls only access tokens and is independent from
`RefreshTokenLifetime`. Use a short lifetime for offline resource servers; five
minutes is the recommended mail-server starting point.

OpenIddict persists protocol entries in both modes. Authorization-code
redemption creates a distinct Identity logical client session. Refresh always
validates that session online. When an OAuth access token is used against a
Hello account/admin API, the composite OAuth handler also validates its `sid`
online. An external offline validator cannot observe a logical-session revoke
until an already-issued JWT expires.

## Resources and scopes

`HelloAuthorizationClientOptions.Resource` assigns one audience to a client.
When it is absent, the client inherits the server-level `Resource`. A request
may omit `resource` or repeat that exact value; it cannot select another
configured resource. Unknown and foreign resources are rejected as protocol
errors.

The built-in scopes are `openid`, `offline_access`, `profile`, `email`, `phone`
and `roles`. Hosts can register up to 32 additional OAuth scope tokens through
`AdditionalScopes`. Names are case-sensitive, must use the RFC 6749 scope-token
character set, and cannot duplicate built-in or other additional scopes. Each
client still receives only its explicitly configured `Scopes`.

The OAuth JWT contains stable Identity `sub`, the logical session `sid`, the
client resource as `aud`, protocol timestamps/identifier and the granted
`scope`. `name` and `preferred_username` are emitted only with `profile`;
`email` and `email_verified` only with `email`; phone and roles follow their
corresponding scopes. Claims come from the configured
`IIdentitySessionClaimsProvider<TProfile>` pipeline. Standard singleton claims
use the last projected value, allowing a host provider to override the default
projection; repeated roles and custom claims remain repeated.

For example, a host profile can make its primary mail address the canonical
mail login without using email as `sub`:

```csharp
public sealed class MailSessionClaimsProvider
    : IIdentitySessionClaimsProvider<HomeProfile>
{
    public Task<IReadOnlyCollection<IdentitySessionClaim>> GetClaimsAsync(
        IdentityUser<HomeProfile> user,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<IdentitySessionClaim>>(
        [
            new(
                IdentitySessionClaimTypes.PreferredUserName,
                user.Profile.PrimaryMailAddress),
        ]);
}

identity.AddSessionClaimsProvider<MailSessionClaimsProvider>();
```

The provider participates in all Identity session claim projection, so keep its
output bounded and free of secrets.

## Ready Server configuration

The production default is disabled. The following is the Home.Auth shape for a
normal portal plus Roundcube/Stalwart:

```json
{
  "SkopkaHello": {
    "AuthorizationServer": {
      "Enabled": true,
      "Issuer": "https://id.example.net",
      "Resource": "skopka-hello-api",
      "AccessTokenFormat": "SelfContainedJwt",
      "AccessTokenLifetime": "00:05:00",
      "AdditionalScopes": ["mail"],
      "SigningCertificatePath": "/run/secrets/oauth-signing.pfx",
      "EncryptionCertificatePath": "/run/secrets/oauth-encryption.pfx",
      "Clients": [
        {
          "ClientId": "home-portal",
          "DisplayName": "Home Portal",
          "Type": "Confidential",
          "ClientSecret": "from-a-secret-manager",
          "Resource": "skopka-hello-api",
          "RedirectUris": [
            "https://home.example.net/signin-oidc"
          ],
          "PostLogoutRedirectUris": [
            "https://home.example.net/signout-callback-oidc"
          ],
          "Scopes": [
            "openid",
            "offline_access",
            "profile",
            "email",
            "roles"
          ]
        },
        {
          "ClientId": "roundcube",
          "DisplayName": "Roundcube Webmail",
          "Type": "Confidential",
          "ClientSecret": "from-a-secret-manager",
          "Resource": "stalwart",
          "RedirectUris": [
            "https://webmail.example.net/index.php/login/oauth"
          ],
          "Scopes": [
            "openid",
            "offline_access",
            "profile",
            "email",
            "mail"
          ]
        }
      ]
    }
  }
}
```

Do not put production secrets in `appsettings.json`. Supply passwords and
client secrets through secret configuration, for example:

```text
SkopkaHello__AuthorizationServer__SigningCertificatePassword=<secret>
SkopkaHello__AuthorizationServer__EncryptionCertificatePassword=<secret>
SkopkaHello__AuthorizationServer__Clients__0__ClientSecret=<secret>
SkopkaHello__AuthorizationServer__Clients__1__ClientSecret=<secret>
```

Both PFX files must contain private keys and remain stable across replicas and
restarts. The signing certificate must contain an asymmetric signing key so its
public key can be published through JWKS; JWKS never exposes private material.
The encryption certificate still protects non-access-token protocol artifacts.
Replacing protocol certificates can invalidate active grants, so plan overlap
or a client-session reset according to the certificate strategy supported by
the host.

Development uses ephemeral protocol keys only when both certificate paths are
absent. The checked-in Development clients and secrets are public test data,
are excluded from publish and Docker output, and must not be copied into a
deployment.

Allowed redirects are exact HTTPS URIs, loopback HTTP for installed apps, and
reverse-domain custom schemes for public clients. Non-loopback HTTP, URI
credentials and fragments are rejected at startup. `Issuer` must be the stable
public HTTPS origin and must be identical on every replica.

Clients that initiate OpenID Connect logout must register every exact return
address in `PostLogoutRedirectUris`. The end-session endpoint revokes the
current Hello browser session, clears its cookies and then returns only to a
registered address supplied with a valid OpenID Connect logout request.

The ready Server stores OpenIddict applications, authorizations and tokens in
the dedicated `skopka_hello_oauth` PostgreSQL schema. Run `--migrate` after any
client configuration change. That command applies the schema and makes the
stored client set exactly match configuration; removing a client deletes its
OpenIddict registration and grants.

## Composite bearer validation

The composed bearer policy supports the existing Skopka.Identity JWT, OAuth
reference access tokens and OAuth JWT access tokens. It uses an untrusted JWT
header/payload only to choose a candidate handler. The selected handler still
must validate signature, exact issuer, Hello API audience, expiry and token
type. OAuth tokens accepted by Hello additionally validate the Identity logical
session online. A mail token with `aud=stalwart` is not valid for a Hello API.

## Deliberate limits

This is a first-party server: configured clients receive implicit consent after
the user has an active Hello browser session. There is no third-party consent
screen, dynamic client registration, device authorization, password/client
credentials grant, token exchange, UserInfo or introspection endpoint.
Native clients use system-browser authorization and PKCE; they must not collect
a Hello password or accept a provider token in JSON.

See [mail OIDC integration](mail-oidc-integration.md) for the Roundcube 1.7 and
Stalwart 0.16 deployment example.
