# Roundcube and Stalwart OIDC integration

This example connects Roundcube 1.7 to Home.Auth as a confidential OIDC client
and passes the resulting access token to Stalwart 0.16 over XOAUTH2 or
OAUTHBEARER. Replace every `example.net` value and placeholder secret; they are
not production defaults.

The flow is:

1. Roundcube starts authorization code with PKCE S256 at Home.Auth.
2. Home.Auth authenticates the browser and returns a code to Roundcube's exact
   HTTPS callback.
3. Roundcube redeems the code with `client_secret_post` and receives an ID
   token, signed JWT access token and opaque rotating refresh token.
4. Roundcube uses `preferred_username` as the mail login and sends the access
   token to the fixed IMAP/SMTP host with XOAUTH2 or OAUTHBEARER.
5. Stalwart resolves Home.Auth discovery/JWKS, validates the JWT offline and
   requires the `stalwart` audience plus `openid`, `email` and `mail` scopes.

Configure Home.Auth as shown in
[authorization server](authorization-server.md), including:

```json
{
  "AccessTokenFormat": "SelfContainedJwt",
  "AccessTokenLifetime": "00:05:00",
  "AdditionalScopes": ["mail"],
  "Clients": [
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
```

Run the ready Server's `--migrate` command after adding or changing the client.
Keep the secret out of source and inject the same value into Home.Auth and
Roundcube.

## Stalwart

Create an OIDC Directory in Stalwart and select it as the active authentication
directory. The equivalent object is:

```json
{
  "@type": "Oidc",
  "description": "Home.Auth",
  "issuerUrl": "https://id.example.net",
  "requireAudience": "stalwart",
  "requireScopes": {
    "openid": true,
    "email": true,
    "mail": true
  },
  "claimUsername": "preferred_username",
  "claimName": "name"
}
```

Do not configure UserInfo or introspection credentials. With a valid
self-contained access token, Stalwart reads discovery and JWKS and performs
offline signature, issuer, audience, lifetime and claim validation. Home.Auth
does not expose UserInfo or introspection endpoints.

The value of `preferred_username` must exactly match the pre-provisioned
Stalwart principal. If the canonical mail address lives in the host-defined
profile rather than `IdentityUser.UserName`, register the custom claims provider
shown in [authorization server](authorization-server.md). `sub` remains the
stable Identity user id and is never replaced by an email address.

Provision the Stalwart principal and primary mail address before the first
login. OIDC authentication alone is not an offline directory: without
pre-provisioning, inbound mail for a user who has never authenticated may be
rejected as an unknown recipient. Use the Stalwart Web UI, CLI/JMAP API or a
dedicated provisioning service; do not copy password hashes.

## Roundcube

Add the following values to Roundcube's `config.inc.php`. Do not edit
`defaults.inc.php`, and obtain the secret from the deployment secret store.

```php
$config['imap_host'] = 'ssl://mail.example.net:993';
$config['smtp_host'] = 'tls://mail.example.net:587';

$config['oauth_provider'] = 'generic';
$config['oauth_provider_name'] = 'Home.Auth';
$config['oauth_client_id'] = 'roundcube';
$config['oauth_client_secret'] = getenv('ROUNDCUBE_OAUTH_CLIENT_SECRET');
$config['oauth_config_uri'] =
    'https://id.example.net/.well-known/openid-configuration';
$config['oauth_pkce'] = 'S256';
$config['oauth_scope'] =
    'openid offline_access profile email mail';
$config['oauth_identity_fields'] = ['preferred_username', 'email'];
$config['oauth_login_redirect'] = true;
$config['oauth_auth_type'] = 'OAUTH';
$config['oauth_cache'] = 'db';
```

`oauth_auth_type = 'OAUTH'` keeps Roundcube's automatic choice between XOAUTH2
and OAUTHBEARER. Set it explicitly to `XOAUTH2` or `OAUTHBEARER` only after
confirming the mechanism advertised by both fixed mail endpoints. Keep TLS peer
verification enabled and ensure the reverse proxy tells Roundcube that its
public callback is HTTPS.

Roundcube 1.7 uses discovery when `oauth_config_uri` is present and sends its
confidential secret in the token request body. Home.Auth still requires PKCE
S256 for this confidential client. The exact callback configured in both
systems is:

```text
https://webmail.example.net/index.php/login/oauth
```

## Revocation and operations

Home.Auth validates the logical session on every refresh. Revoking it prevents
new tokens immediately. Hello account/admin APIs also reject the OAuth token
immediately because they perform online `sid` validation.

Stalwart is an offline validator and has no revocation callback or
introspection. A JWT already issued before revocation can therefore remain
usable there only until `exp`; with the recommended configuration that window
is at most five minutes, plus validator clock skew. Shorten the lifetime if the
mail risk model requires a smaller window.

Before production, verify:

- discovery reports the public HTTPS issuer and a reachable `jwks_uri`;
- JWKS contains the public asymmetric signing key and no private parameters;
- the access token is a three-segment JWS with `typ=at+jwt`, `aud=stalwart` and
  all required scopes;
- the ID token is used only by Roundcube and is rejected as an IMAP/SMTP access
  token;
- refresh produces a different opaque reference token;
- both IMAP and SMTP authenticate with the fixed host and expected SASL method;
- provisioning creates the exact `preferred_username` before inbound mail is
  accepted;
- certificate/JWKS rotation is exercised with the real Stalwart cache behavior;
- logical-session revoke stops refresh immediately and mail access after the
  documented access-token expiry window.

Roundcube's upstream OAuth configuration documents the generic provider,
discovery, PKCE, identity fields, fixed hosts and `client_secret_post` behavior:
<https://github.com/roundcube/roundcubemail/wiki/Configuration%3A-OAuth2>.
Stalwart's upstream OIDC directory fields and pre-provisioning limitation are
documented at <https://stalw.art/docs/auth/backend/oidc/>.
