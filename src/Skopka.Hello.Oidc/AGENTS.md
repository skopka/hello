# OAuth/OIDC Module Instructions

Read `../../AGENTS.md` first.

Do not implement OAuth/OIDC protocol behavior by hand. Before adding a maintained
library, verify .NET 10 support and document the threat model. The adapter owns
state, nonce, PKCE and callback/token validation. Only a fully validated
provider/subject may be passed to Skopka.Identity, and matching email alone never
authorizes account linking.

Provider ids are stable application configuration keys, not discovered issuers.
Keep subjects exact and case-sensitive. The raw callback writes only a
short-lived external ticket; a same-origin antiforgery-protected POST promotes
it to a `SameSite=Strict` pending ticket. Bind link tickets to the authenticated
user and session. Derive unlink targets and optimistic versions from the online
Identity sign-in-method snapshot, and recheck the last-enabled-method rule after
OTP verification. Provider tokens and subjects never leave this module.

Every terminal external or pending POST consumes its random protected flow id
through `IHelloOidcFlowStore` before it can create a session or mutate an
account. Retryable validation failures rotate to a new id without extending the
absolute ticket expiry. The default store uses Identity's persistent rate
limiter when present and otherwise fails closed through a bounded process-local
fallback. Multi-replica hosts must retain the persistent limiter or replace the
store with an atomic shared implementation.
