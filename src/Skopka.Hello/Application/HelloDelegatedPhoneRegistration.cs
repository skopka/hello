using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.PhoneOtp;
using Skopka.Identity.Registration;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;

namespace Skopka.Hello;

/// <summary>Host policy authorizing an actor to create a separate account using a verified phone.</summary>
public interface IHelloDelegatedPhoneRegistrationPolicy<TProfile>
{
    /// <summary>Allows or rejects this actor; all registered policies must allow the operation.</summary>
    Task<OperationResult> AuthorizeAsync(HelloAccount<TProfile> actor, CancellationToken cancellationToken);
}

/// <summary>Starts and completes server-side delegated phone registration without changing the actor session.</summary>
public interface IHelloDelegatedPhoneRegistration<TProfile>
{
    /// <summary>Sends a code through the configured SMS channel and returns an actor-session-bound ticket.</summary>
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, CancellationToken cancellationToken);
    /// <summary>Starts verification with a response for configured pre-send CAPTCHA policies.</summary>
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, string? captchaResponse, CancellationToken cancellationToken);
    /// <summary>Verifies the code and atomically registers a separate confirmed-phone account without changing the actor's session.</summary>
    Task<OperationResult<HelloAccount<TProfile>>> CompleteAsync(string accessToken, string ticket, string code, string? clientKey,
        TProfile profile, bool acceptTermsOfService, bool acceptPrivacyPolicy, CancellationToken cancellationToken);
}

internal sealed class HelloDelegatedPhoneRegistration<TProfile>(
    IHelloPhoneOtpApplication<TProfile> sender, IIdentityPhoneOtpService<TProfile> phoneOtp,
    IIdentityRegistrationService<TProfile> registration, IEnumerable<IHelloAccessTokenValidator<TProfile>> validators,
    IEnumerable<IHelloDelegatedPhoneRegistrationPolicy<TProfile>> policies, HelloPhoneInputPolicy phonePolicy,
    IHelloRegistrationConsentPolicy consentPolicy, HelloRegistrationAdmission<TProfile> admission,
    IDataProtectionProvider protection, TimeProvider clock,
    IHelloRegistrationConsentProfileEnricher<TProfile>? enricher = null,
    IHelloRegistrationContinuation<TProfile>? continuation = null) : IHelloDelegatedPhoneRegistration<TProfile>
{
    private readonly IDataProtector protector = protection.CreateProtector("Skopka.Hello.DelegatedPhoneRegistration.v1");
    private sealed record Ticket(Guid ActorId, string TokenHash, string Phone, Guid ChallengeId, DateTimeOffset ExpiresAt);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Error Invalid() => new(IdentityErrorCodes.VerificationProofInvalid, "Phone registration request is invalid or expired.", ErrorType.Unauthorized);
    private static HelloAccount<TProfile> Account(IdentityUser<TProfile> u) => new(u.Id, u.Flags, u.UserName, u.Email, u.EmailConfirmed, u.Phone, u.PhoneConfirmed, u.Profile, u.Version, u.CreatedAt, u.ModifiedAt);

    private async Task<OperationResult<HelloAccount<TProfile>>> AuthorizeAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(token, ct);
            if (!result.IsSuccess) continue;
            var actor = Account(result.Value);
            var registeredPolicies = policies.ToArray();
            if (registeredPolicies.Length == 0) break;
            foreach (var policy in registeredPolicies)
            {
                var authorized = await policy.AuthorizeAsync(actor, ct);
                if (!authorized.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(authorized.Errors);
            }
            return OperationResultFactory.Success(actor);
        }
        return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
    }

    public Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, CancellationToken cancellationToken)
        => BeginAsync(accessToken, phone, clientKey, null, cancellationToken);

    public async Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, string? captchaResponse, CancellationToken cancellationToken)
    {
        var actor = await AuthorizeAsync(accessToken, cancellationToken);
        if (!actor.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(actor.Errors);
        var normalized = phonePolicy.Normalize(phone, otpRequest: true);
        if (!normalized.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(normalized.Errors);
        var issued = await sender.RequestCodeAsync(normalized.Value, clientKey, captchaResponse, cancellationToken);
        if (!issued.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(issued.Errors);
        var ticket = new Ticket(actor.Value.Id, Hash(accessToken), normalized.Value, issued.Value.RequestId, clock.GetUtcNow().AddMinutes(5));
        return OperationResultFactory.Success(new HelloPhoneBindingChallenge(protector.Protect(JsonSerializer.Serialize(ticket)), issued.Value.RetryAfter));
    }

    public async Task<OperationResult<HelloAccount<TProfile>>> CompleteAsync(string accessToken, string ticket, string code, string? clientKey,
        TProfile profile, bool acceptTermsOfService, bool acceptPrivacyPolicy, CancellationToken cancellationToken)
    {
        var actor = await AuthorizeAsync(accessToken, cancellationToken);
        if (!actor.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(actor.Errors);
        Ticket? pending;
        try { pending = string.IsNullOrWhiteSpace(ticket) || ticket.Length > 8192 ? null : JsonSerializer.Deserialize<Ticket>(protector.Unprotect(ticket)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException) { pending = null; }
        if (pending is null || pending.ExpiresAt <= clock.GetUtcNow() || pending.ActorId != actor.Value.Id || pending.TokenHash != Hash(accessToken))
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
        var consent = consentPolicy.Validate(new HelloRegistrationConsent(acceptTermsOfService, acceptPrivacyPolicy,
            acceptTermsOfService || acceptPrivacyPolicy ? clock.GetUtcNow() : null));
        if (!consent.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(consent.Errors);
        if (enricher is not null)
        {
            var enriched = enricher.Enrich(profile, consent.Value);
            if (!enriched.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(enriched.Errors);
            profile = enriched.Value;
        }
        if (continuation is not null)
        {
            var prepared = await continuation.PrepareProfileAsync(profile, HelloRegistrationKind.DelegatedPhoneOtp, cancellationToken);
            if (!prepared.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(prepared.Errors);
            profile = prepared.Value;
        }
        var admitted = await admission.CheckAsync(HelloRegistrationKind.DelegatedPhoneOtp, cancellationToken);
        if (!admitted.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(admitted.Errors);
        var proof = await phoneOtp.VerifyForBindingAsync(new VerifyPhoneOtpCommand(pending.ChallengeId, code, clientKey), cancellationToken);
        if (!proof.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(proof.Errors);
        var created = await registration.RegisterVerifiedPhoneAsync(new RegisterVerifiedPhoneUserCommand<TProfile>(
            new CreateUserCommand<TProfile>(null, null, pending.Phone, profile), proof.Value), cancellationToken);
        return created.IsSuccess ? OperationResultFactory.Success(Account(created.Value)) : OperationResultFactory.Fail<HelloAccount<TProfile>>(created.Errors);
    }
}
