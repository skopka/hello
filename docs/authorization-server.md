# OAuth/OIDC authorization server

`Skopka.Hello.AuthorizationServer` is an optional OpenIddict-based authorization
server for trusted, pre-registered native and BFF clients. It is separate from
`Skopka.Hello.Oidc`, which is the client adapter used when Hello signs users in
through an external provider.

The implemented grant is authorization code with mandatory PKCE. Public native
clients do not have a secret. Confidential BFF clients require both PKCE and a
secret. Request `offline_access` to receive a rotating reference refresh token.
Access and refresh tokens are persisted by OpenIddict; every token also carries
an Identity logical `sid`. Authorization-code redemption creates a distinct
logical client session, and access-token authentication plus refresh both
validate that session online. Revoking the session therefore immediately blocks
Hello account and admin APIs and prevents further refresh.

The server exposes OpenID Connect discovery plus:

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/connect/authorize` | Browser SSO and authorization-code issuance |
| `POST` | `/connect/token` | Code redemption and refresh-token rotation |

The ready Server stores OpenIddict applications, authorizations and tokens in
the dedicated `skopka_hello_oauth` PostgreSQL schema. Run `--migrate` after any
client configuration change. That command applies the schema and makes the
stored client set exactly match configuration; removing a client from
configuration deletes its OpenIddict registration.

## Ready Server configuration

The production default is disabled. Enable it and register exact redirect URIs:

```json
{
  "SkopkaHello": {
    "AuthorizationServer": {
      "Enabled": true,
      "Issuer": "https://identity.example.com",
      "Resource": "skopka-hello-api",
      "SigningCertificatePath": "/run/secrets/oauth-signing.pfx",
      "EncryptionCertificatePath": "/run/secrets/oauth-encryption.pfx",
      "Clients": [
        {
          "ClientId": "example-native",
          "DisplayName": "Example native app",
          "Type": "Public",
          "RedirectUris": ["com.example.app:/oauth/callback"],
          "Scopes": ["openid", "offline_access", "profile"]
        },
        {
          "ClientId": "example-bff",
          "DisplayName": "Example BFF",
          "Type": "Confidential",
          "ClientSecret": "from-a-secret-manager",
          "RedirectUris": ["https://app.example.com/signin-oidc"],
          "Scopes": ["openid", "offline_access", "profile", "email", "roles"]
        }
      ]
    }
  }
}
```

Supply certificate and client-secret passwords through secret configuration:

```text
SkopkaHello__AuthorizationServer__SigningCertificatePassword=<secret>
SkopkaHello__AuthorizationServer__EncryptionCertificatePassword=<secret>
SkopkaHello__AuthorizationServer__Clients__1__ClientSecret=<secret>
```

Both PFX files must contain private keys and remain stable across replicas and
restarts. Replacing either certificate invalidates protocol artifacts protected
with the old key, so perform a planned client-session reset when rotating them.
Development uses ephemeral protocol keys only when both certificate paths are
absent. The checked-in Development clients and BFF secret are public test data,
are excluded from publish and Docker output, and must not be copied into a
deployment.

Allowed redirects are HTTPS, loopback HTTP for installed apps, and reverse-domain
custom schemes for public clients. Non-loopback HTTP, URI credentials and
fragments are rejected at startup. `Issuer` must be the stable public HTTPS
origin and must be identical on every replica.

## Deliberate MVP limits

This is a first-party server: configured clients receive implicit consent after
the user has an active Hello browser session. There is no third-party consent
screen, dynamic client registration, device authorization, password/client
credentials grant, user-info endpoint, logout endpoint or token introspection
endpoint. Native clients use system-browser authorization and PKCE; they must
not collect a Hello password or accept a provider token in JSON.
