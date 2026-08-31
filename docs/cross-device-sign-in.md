# Cross-device sign-in

Cross-device sign-in lets a browser that is not authenticated (device B) ask
for explicit approval from an existing authenticated browser (device A).
The feature is disabled by default and does not replace password, email or
OIDC sign-in.

## Flow

1. B creates a two-minute request and receives an HttpOnly browser-verifier
   cookie, QR approval URL and short visual code.
2. A opens the QR URL or enters the short code on the authenticated
   `Sign-in requests` page, signs in if necessary, compares the same code and
   sees the display-only IP, User-Agent/device description and creation time.
3. A starts a fresh TOTP step-up challenge and explicitly approves or denies.
4. B polls only the request state. After approval it consumes the request once.
5. Skopka.Identity creates a new session through the existing
   `IIdentitySessionService<TProfile>.CreateAsync`; Hello writes it through the
   existing `IHelloSessionCookieManager`.
6. B continues the stored local return URL, including a local
   `/connect/authorize?...` request, or the configured authenticated home.

A's cookie, access token and refresh token never move to B and A's session is
not revoked or modified by approval.

## Registration

Cross-device registration must happen after `AddSkopkaHello` and before
`AddSkopkaHelloUi`, because the UI package conditionally contributes its
Razor application only when the feature is enabled:

```csharp
var identity = services.AddSkopkaHello<AppProfile>(options =>
{
    options.PublicOrigin = new Uri("https://accounts.example.com");
    options.Totp.Enabled = true;
});

identity
    .UsePostgreSql(connectionString)
    .UseJwtSessions(currentKeyVersion, signingKeys)
    .UseDataProtectionTotp()
    .UseHmacRateLimiting(currentRateLimitKeyVersion, rateLimitKeys)
    .AddCrossDeviceSignIn(options =>
    {
        options.RequestLifetime = TimeSpan.FromMinutes(2);
        options.PollingInterval = TimeSpan.FromSeconds(2);
        options.UserCodeLength = 8;
        options.UserCodeGroupSize = 4;
        options.StepUpMaximumAge = TimeSpan.FromMinutes(2);
        options.CreateClientPermitLimit = 5;
        options.CreateClientWindow = TimeSpan.FromMinutes(5);
        options.StatusClientPermitLimit = 120;
        options.StatusClientWindow = TimeSpan.FromMinutes(2);
        options.SessionClientName = "My application";
        options.RetentionAfterExpiration = TimeSpan.FromDays(1);
        options.CleanupBatchSize = 500;
    });

services.AddSkopkaHelloUi<AppProfile, AppProfileUiFactory>();
```

This version deliberately requires:

- `Enabled = true` (set by `AddCrossDeviceSignIn` unless explicitly disabled);
- HTTPS `SkopkaHelloOptions.PublicOrigin`;
- secure Hello cookies;
- enabled TOTP and `RequireStepUp = true`;
- `StepUpMethod = "totp"`.

Invalid combinations fail at startup. Existing hosts that do not call
`AddCrossDeviceSignIn` receive no new endpoints, Razor routes or login button.

The ready Server binds the same values from
`SkopkaHello:CrossDeviceSignIn`; its default `Enabled` value is `false`. When
enabled, it also runs hourly bounded pruning.

## HTTP and Razor surface

The optional Minimal API surface is:

| Method | Path | Authentication |
| --- | --- | --- |
| `POST` | `/auth/cross-device` | Anonymous; begins B request |
| `GET` | `/auth/cross-device/{deviceCode}/status` | B verifier cookie |
| `POST` | `/auth/cross-device/{deviceCode}/complete` | B verifier cookie; issues the normal session cookies |
| `GET` | `/account/cross-device/{deviceCode}` | Bearer for A |
| `POST` | `/account/cross-device/{deviceCode}/challenge` | Bearer for A |
| `POST` | `/account/cross-device/{deviceCode}/approve` | Bearer + current TOTP for A |
| `POST` | `/account/cross-device/{deviceCode}/deny` | Bearer for A |

The packaged UI adds a localized login action, B waiting/QR/timer page, A
short-code request lookup page and approval page at the configured
`UiPathPrefix`. English and Russian strings are built in. QR SVG is generated
locally with QRCoder and contains only the HTTPS approval URL plus the random
public device code. Short-code lookup returns a request only when exactly one
unexpired pending row matches, so a rare collision fails closed.

The begin and status responses never expose the browser verifier or user
identity. The verifier is bound to B in the
`__Host-Skopka.Hello.CrossDevice` cookie with `Secure`, `HttpOnly`, root path
and `SameSite=Strict` defaults. Every Razor POST uses ASP.NET Core
antiforgery. API approval revalidates A's bearer token online before touching
the request.

## Return URLs and OIDC

Only local absolute (`/path`) or application-relative (`~/path`) return URLs
are stored. Scheme-relative, backslash-prefixed, control-character and long
values are rejected before persistence. The UI performs the local check again
before redirecting. This permits the original first-party
`/connect/authorize` request to restart after B receives its own session,
without accepting an external redirect target.

## Persistence, cleanup and deployment

Requests live in Skopka.Identity's provider-specific
`device_authorization_requests` table. Deploy and apply the corresponding
Skopka.Identity PostgreSQL or SQLite `AddDeviceAuthorization` migration after
`AddTotpFactors` and before enabling Hello on any instance. Hello adds no
separate migration for this flow. All replicas must share that database, the
logical session store, Data Protection keys and persistent HMAC rate limiter.

Custom hosts should periodically call
`IIdentityDeviceAuthorizationService<TProfile>.PruneAsync`. A request left in
the fail-safe `Consuming` state after an uncertain database/session failure
cannot issue a duplicate session; B starts a new request and the retained row
is removed after the configured retention period.

## Security considerations

- The device code is a 256-bit random lookup value; the visual user code is
  only for comparison and cannot complete a request.
- The raw 256-bit browser verifier is never stored; Identity stores its
  SHA-256 hash and compares hashes in fixed time.
- Approval checks an exact user/action/device-code TOTP step-up decision with
  a short freshness window.
- Consumption rechecks expiry, verifier, user state and the approval-time
  security stamp, then atomically reserves one session creation.
- IP, User-Agent and normalized device descriptions are untrusted display
  hints. They must not be used as authentication factors.
- Do not log device codes, verifier cookies, TOTP values, access/refresh
  tokens or QR contents. Security observer events cover creation, approval,
  denial, expiry and consumption without those secrets.
