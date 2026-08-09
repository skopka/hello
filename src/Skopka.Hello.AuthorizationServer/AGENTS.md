# Authorization Server Module Instructions

Read `../../AGENTS.md` first.

Use OpenIddict for every OAuth 2.0/OpenID Connect protocol operation. This
module supports pre-registered first-party public native clients and
confidential BFF clients through authorization code with PKCE and optional
`offline_access`. Do not add implicit, password, device, client-credentials or
dynamic-registration flows without a separate threat-model review.

The authorization endpoint authenticates through the configured local browser
scheme. Authorization codes retain only the source logical session id. Code
redemption creates a separate `IIdentitySessionRegistry<TProfile>` session;
refresh redemption validates that session online before issuing new tokens.
Never copy a security stamp, browser access token, provider token or client
secret into claims.

Reference access tokens are the compatibility default. Self-contained access
tokens must be signed asymmetric JWSs with access-token encryption disabled;
authorization codes and rotating refresh tokens stay reference tokens. Assign
one configured resource to each client, reject request-selected alternatives
and keep Hello API validation audience-bound plus online-session-bound. An
offline external validator can observe revocation only when the short-lived JWT
expires.

Client ids, exact redirect URIs and scopes are operator configuration. Public
clients never have secrets and always require PKCE. Confidential clients must
authenticate at the token endpoint and also require PKCE. Consent is implicit
only for these pre-registered first-party clients; there is no third-party
consent or authorization UI in this module.
