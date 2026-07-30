# OAuth/OIDC Module Instructions

Read `../../AGENTS.md` first.

Do not implement OAuth/OIDC protocol behavior by hand. Before adding a maintained
library, verify .NET 10 support and document the threat model. The adapter owns
state, nonce, PKCE and callback/token validation. Only a fully validated
provider/subject may be passed to Skopka.Identity, and matching email alone never
authorizes account linking.
