# Mandatory second factor at sign-in

A host can register one or more `IHelloSignInSecondFactorPolicy<TProfile>` services. Each
is evaluated against the current Identity user; if any requires a second factor, Hello
requires a TOTP authenticator or recovery code. An empty set leaves sign-in behavior
unchanged. The policy must consult current server-side membership when roles control
the rule; never trust role or assurance values from a request.

Enable Hello TOTP and Identity's TOTP provider and register Identity's persistent rate
limiter. The admission gate fails closed if required services are unavailable. Share the
Data Protection key ring and the replay guard across replicas; the built-in guard uses
the persistent limiter when available. A short-lived protected continuation is a bearer
secret: protect it in transit and storage and do not log it. No access or refresh token
is issued while a primary authentication waits for the second factor.

The flow inspects the continuation, then begins verification or enrollment using POST.
Identity verifies the current TOTP or a one-use recovery code with bounded attempts.
New enrollment displays the recovery codes once and requires an explicit acknowledgment
before a session is issued. Losing the response after enrollment requires restarting
sign-in; the factor remains enabled. Existing sessions without trusted TOTP assurance
cannot satisfy a newly applicable rule and must sign in again.

Session assurance is recorded on the Identity logical session and checked against the
live policy on session creation, refresh and online validation. OAuth and native derived
sessions inherit assurance only from a validated source. Stateless offline JWT
validation cannot enforce later policy changes until token expiry. The generic Identity
device authorization flow does not attest its approval factor, so it fails closed when
the policy requires TOTP until a fresh verified sign-in completes. Host-supplied custom
session stores and token validators must preserve and report assurance as documented by
Identity; unsupported implementations default to no assurance.

## Registration

Register a scoped `IHelloSignInSecondFactorPolicy<TProfile>` implementation, enable
`options.Totp.Enabled` in `AddSkopkaHello<TProfile>`, and call
`UseDataProtectionTotp()` on the Identity builder alongside the existing persistent
rate limiter. A complete role-policy example for IqZone is in the
[upgrade guide](upgrade-0.28.0.md). The policy interface is:

```csharp
Task<bool> RequiresAsync(IdentityUser<TProfile> user, CancellationToken cancellationToken);
```

An account's role lookup must not fail open: do not return `false` merely because
the role query failed. Use a policy implementation that rejects or requires the
additional factor in that case. Hosts retain ownership of staff role names.

## API continuation

The normal first-factor endpoint returns HTTP 401 with ProblemDetails code
`hello.sign_in.second_factor_required` and a `secondFactor` object containing
`ticket` and `expiresAt`. This is a successful first factor awaiting another step,
not a session. Existing clients safely receive no access token or refresh cookie.
All following requests are JSON POSTs; tickets never belong in URLs.

| Endpoint | Body | Successful response |
| --- | --- | --- |
| `/auth/second-factor/inspect` | `{ "ticket": "…" }` | Stage and expiry |
| `/auth/second-factor/verification` | `{ "ticket": "…" }` | Replacement ticket and expiry |
| `/auth/second-factor/verification/complete` | `{ "ticket": "…", "code": "…" }` | Normal session response |
| `/auth/second-factor/enrollment` | `{ "ticket": "…" }` | Replacement ticket, secret, provisioning URI and expiry |
| `/auth/second-factor/enrollment/confirm` | `{ "ticket": "…", "code": "…" }` | Replacement acknowledgment ticket, recovery codes and expiry |
| `/auth/second-factor/enrollment/acknowledge` | `{ "ticket": "…" }` | Normal session response |

Use the replacement ticket from each successful step, not the original ticket.
For an existing authenticator, begin verification and submit a current TOTP or
one unused recovery code. Wrong-code errors leave the verification step retryable
within Identity's limits. For enrollment, configure the authenticator from the
locally rendered QR or provisioning secret, confirm its code, save the returned
recovery codes, then acknowledge. Only completion/acknowledgment writes the usual
refresh cookie and returns the access token. Refresh tokens never appear in JSON.

Do not retry a successful terminal request: it is one-use. If a response is lost,
restart sign-in. All responses carrying continuation or enrollment data have
`Cache-Control: no-store` and `Referrer-Policy: no-referrer`. HTTP body logging must
remain disabled/redacted for authentication endpoints, including these routes.

## Razor and host composition

The Login page presents the extra steps under the same immutable UI prefix.
Password, phone, passkey and external sign-in enter the same flow. Forms retain
antiforgery protection and accept only local return URLs. English and Russian
strings are included; enrollment QR generation is local and does not contact a
third-party service. Recovery codes are shown before the session is established.

This works in an in-process Razor/Blazor Server host using the Hello UI cookie.
The host must not replace the session admission/online validation services with
custom implementations that skip these checks. The ready Server image is not
required for this composition.
