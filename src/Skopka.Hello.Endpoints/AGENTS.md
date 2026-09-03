# Endpoint Module Instructions

Read `../../AGENTS.md` first.

This package owns Minimal API routes, HTTP DTOs and the single
OperationResult-to-ProblemDetails mapper. Shared application orchestration and
refresh/antiforgery cookie mechanics live in `Skopka.Hello` so Razor UI and API
cannot diverge.

Call only Skopka.Identity application services. Never inject stores or
DbContexts. Keep login responses enumeration-safe, never serialize a refresh
token, derive client context server-side and require authorization independently
from any future step-up decision.

Password-reset and email/phone-confirmation request endpoints return `202` for
known and unknown well-formed contacts. Token application endpoints map structured
Identity failures normally. Do not put action tokens, recipient addresses or
passwords in logs or response details.

Password-change endpoints remain independently bearer-authorized and perform
online token validation in the shared application operation. Never accept the
step-up action, binding, user id, recipient address or expected version from an
HTTP DTO, and never return the OTP delivery code.

External-provider catalog and linked-provider responses contain only stable
provider ids, display names and safe metadata. Never expose the provider subject
or protocol tokens. External link/unlink stays in the antiforgery-protected
browser flow unless a separately designed native-client proof flow is added.

Map the anonymous password-registration endpoint only when the shared startup
self-registration policy is enabled. Do not use an endpoint-local flag or leave
the route in OpenAPI when disabled; operation-level enforcement in the facade
remains the defense-in-depth boundary.

Passkey routes are mapped only when the identity builder registered the WebAuthn
service, not behind an endpoint-local flag: a route a host has no service for is
a route that answers with a missing dependency. Both halves of signing in are
anonymous, because a credential identifies itself and there is nobody to
authorize until it has. Binary fields travel base64url and are decoded before
anything is looked up. Credential responses carry a label and dates only; never
the public key, the credential identifier or the authenticator model.
