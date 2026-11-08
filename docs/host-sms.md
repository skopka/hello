# Host SMS and delegated phone registration

`IHelloSmsSender` is a trusted server-side service for a short host-authored SMS. `SendAsync` accepts a normalized/normalizable phone, text of at most 480 characters, a trusted client key and cancellation token. It uses the configured Hello SMS provider and the same global daily SMS budget and Identity phone/client daily buckets as phone OTP. The built-in development logging provider prints text only in Development. The durable Server outbox encrypts the message payload. Do not expose arbitrary-text sending to an unauthenticated endpoint, and make sure custom providers and host telemetry do not log message text or recipient data.

For an authenticated actor to create a different account after proof of the recipient phone, register a host `IHelloDelegatedPhoneRegistrationPolicy<TProfile>`. With no policy the operation is denied. The policy is checked at begin and completion. The host calls `IHelloDelegatedPhoneRegistration<TProfile>.BeginAsync` with the actor's online access token, phone and trusted client key; an overload accepts the CAPTCHA response for configured pre-send policies. Deliver the returned protected ticket only to that actor's session. Complete with the ticket, SMS code, host profile and consent flags. Identity creates the confirmed-phone account atomically; the actor session is unchanged. A number's occupancy is disclosed only after the correct code. Tickets are bound to the initiating access token, expire and cannot be replayed. Registration admission, consent requirements and optional profile/continuation hooks still apply. This host-authorized operation is separate from self-registration and does not require `SelfRegistrationEnabled`.

The verification code comes from the configured Identity verification provider. The built-in HMAC provider generates random codes; it has no fixed-code option. A staging host that needs a fixed code must explicitly supply a suitable test-only `IVerificationMethodProvider` and keep it out of production. The development logging SMS provider only logs the code it receives; it does not choose it.

## Delegated phone verification

Delegated registration only creates a new account. When the number may already belong to an account, for example a parent adding a child who registered earlier, use `IHelloDelegatedPhoneVerification<TProfile>`. It proves that the signed-in actor possesses the number and leaves the decision to the host: register a new account on it or link the existing one.

It is authorized by the same `IHelloDelegatedPhoneRegistrationPolicy<TProfile>` at every step, sends codes through the same path (SMS budget, phone/client limits, CAPTCHA and the verification provider are unchanged) and accepts tickets from either service.

1. `BeginAsync(accessToken, phone, clientKey[, captchaResponse])` sends the code and returns the protected ticket. A taken and a free number give the same result.
2. `VerifyAsync(accessToken, ticket, code, clientKey)` verifies the code. Identity errors (`VerificationResponseInvalid`, `VerificationAttemptsExceeded`, ...) are returned unchanged and disclose nothing about the number. After the correct code it returns `HelloPhoneOwnershipProof`: the protected `Proof`, the normalized `Phone`, `VerifiedAt`, `ExpiresAt` and `AccountId`, the active account whose **confirmed** phone is this number (it may be the actor itself), or `null`. An account that carries the number unconfirmed is not reported; registration on that number still fails with `DuplicatePhone`. Blocked accounts are reported; deleted accounts are not.
3. Spend the proof once with one of:
   - `RegisterAsync(accessToken, proof, profile, acceptTermsOfService, acceptPrivacyPolicy)`, which atomically creates a separate confirmed-phone account exactly like `CompleteAsync` (consent, admission, profile enricher and registration continuation apply);
   - `RedeemAsync(accessToken, proof)`, which returns `HelloPhoneOwnership(ActorId, Phone, VerifiedAt, AccountId)`, a trusted fact for linking. `AccountId` is read again at redemption.

```csharp
var proof = await verification.VerifyAsync(accessToken, ticket, code, clientKey, ct);
if (!proof.IsSuccess) return MapCodeErrors(proof.Errors);
if (proof.Value.AccountId is null)
{
    var child = await verification.RegisterAsync(accessToken, proof.Value.Proof, profile, true, true, ct);
    // ...record the parent-child relationship for child.Value.Id
}
else
{
    var ownership = await verification.RedeemAsync(accessToken, proof.Value.Proof, ct);
    if (ownership.IsSuccess && ownership.Value.AccountId is { } childId && childId != ownership.Value.ActorId)
    {
        // ...link ownership.Value.ActorId with childId in host storage
    }
}
```

The proof cannot be forged, replayed or transferred. It is protected with the Data Protection key ring and bound to the actor and the access token that started the flow. It wraps the Skopka.Identity verification proof, which Identity spends atomically in its store on registration or redemption, so a second use fails on every instance. The proof expires with that Identity proof (`VerificationOptions.ProofLifetime`, two minutes by default), so spend it right after the correct code. Because of the token binding, a UI that refreshes the access token between the steps gets an invalid-or-expired result and must restart. Keep the proof in the actor's server-side flow; never put it in a URL or log it, together with the number and `AccountId`. Hello records no relationship and emits no extra security event; the host's own records are the audit trail of a link.
