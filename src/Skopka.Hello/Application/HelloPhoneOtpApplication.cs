using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.PhoneOtp;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal sealed class HelloPhoneOtpApplication<TProfile>(
    IIdentityPhoneOtpService<TProfile> phoneOtp,
    IIdentityRegistrationService<TProfile> registration,
    IIdentitySessionService<TProfile> sessions,
    IHelloAccountMessageSender messageSender,
    IEnumerable<IIdentityRateLimiter<TProfile>> rateLimiters,
    IEnumerable<IHelloPhonePreSendPolicy> preSendPolicies,
    IDataProtectionProvider protectionProvider,
    IHelloRegistrationConsentPolicy consentPolicy,
    SkopkaHelloOptions options,
    TimeProvider timeProvider,
    HelloRegistrationAdmission<TProfile>? registrationAdmission = null,
    IHelloRegistrationConsentProfileEnricher<TProfile>? profileEnricher = null)
    : IHelloPhoneOtpApplication<TProfile>
{
    private readonly IDataProtector protector = protectionProvider
        .CreateProtector("Skopka.Hello.PhoneOtp.Registration.v1");
    private readonly IIdentityRateLimiter<TProfile>? rateLimiter =
        rateLimiters.FirstOrDefault();

    public async Task<OperationResult<HelloPhoneCodeRequest>> RequestCodeAsync(
        string phone,
        string? clientKey,
        string? captchaResponse,
        CancellationToken cancellationToken)
    {
        if (!options.PhoneOtpEnabled)
        {
            return Disabled<HelloPhoneCodeRequest>();
        }

        phone = HelloPhoneNormalizer.Normalize(phone, options.DefaultPhoneRegion);

        var availability = messageSender.CheckAvailability(
            HelloDeliveryChannel.Sms);
        if (!availability.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloPhoneCodeRequest>(
                availability.Errors);
        }

        foreach (var policy in preSendPolicies)
        {
            var decision = await policy.CheckAsync(
                new HelloPhonePreSendContext(
                    phone, clientKey, captchaResponse),
                cancellationToken);
            if (!decision.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloPhoneCodeRequest>(
                    decision.Errors);
            }
        }

        if (rateLimiter is null)
        {
            throw new InvalidOperationException(
                "Phone OTP requires an Identity rate limiter.");
        }

        var budget = await rateLimiter.HitAsync(
            new RateLimitRequest(
                "hello.phone_otp.daily_sms",
                "host",
                options.DailySmsBudget,
                TimeSpan.FromDays(1)),
            cancellationToken);
        if (!budget.IsAllowed)
        {
            return OperationResultFactory.Fail<HelloPhoneCodeRequest>(
                new Error(
                    IdentityErrorCodes.RateLimitExceeded,
                    "Too many requests.",
                    ErrorType.Forbidden,
                    new RateLimitDetails(budget.RetryAfter)));
        }

        var issued = await phoneOtp.BeginAsync(
            new BeginPhoneOtpCommand(phone, clientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloPhoneCodeRequest>(
                issued.Errors);
        }

        var sent = await messageSender.SendAsync(
            new HelloAccountMessage(
                Guid.NewGuid(),
                HelloAccountMessageKind.PhoneSignInVerification,
                HelloDeliveryChannel.Sms,
                issued.Value.Phone,
                null,
                issued.Value.ExpiresAt,
                issued.Value.DeliveryCode,
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName),
            cancellationToken);
        return sent.IsSuccess
            ? OperationResultFactory.Success(
                new HelloPhoneCodeRequest(
                    issued.Value.ChallengeId,
                    issued.Value.RetryAfter))
            : OperationResultFactory.Fail<HelloPhoneCodeRequest>(
                sent.Errors);
    }

    public async Task<OperationResult<HelloPhoneCodeVerification<TProfile>>>
        VerifyCodeAsync(
            Guid requestId,
            string code,
            string? clientKey,
            IdentitySessionMetadata sessionMetadata,
            CancellationToken cancellationToken)
    {
        if (!options.PhoneOtpEnabled)
        {
            return Disabled<HelloPhoneCodeVerification<TProfile>>();
        }

        var result = await phoneOtp.VerifyAsync(
            new VerifyPhoneOtpCommand(requestId, code, clientKey),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloPhoneCodeVerification<TProfile>>(result.Errors);
        }

        if (result.Value.User is { } user)
        {
            var issued = await sessions.CreateAsync(
                new CreateIdentitySessionCommand(
                    user.Id, user.SecurityStamp, sessionMetadata),
                cancellationToken);
            return issued.IsSuccess
                ? OperationResultFactory.Success(
                    new HelloPhoneCodeVerification<TProfile>(
                        new HelloSignIn<TProfile>(
                            ToAccount(user), ToSession(issued.Value)),
                        null))
                : OperationResultFactory.Fail<
                    HelloPhoneCodeVerification<TProfile>>(issued.Errors);
        }

        if (!options.SelfRegistrationEnabled)
        {
            return OperationResultFactory.Fail<
                HelloPhoneCodeVerification<TProfile>>(
                    HelloRegistrationErrors.Disabled());
        }

        var proof = result.Value.RegistrationProof!;
        var ticket = new RegistrationTicket(
            proof.ChallengeId,
            result.Value.Phone,
            proof.Token,
            proof.ExpiresAt);
        var token = protector.Protect(JsonSerializer.Serialize(ticket));
        return OperationResultFactory.Success(
            new HelloPhoneCodeVerification<TProfile>(null, token));
    }

    public async Task<OperationResult<HelloSignIn<TProfile>>>
        CompleteRegistrationAsync(
            HelloPhoneRegistrationCommand<TProfile> command,
            CancellationToken cancellationToken)
    {
        if (!options.PhoneOtpEnabled || !options.SelfRegistrationEnabled)
        {
            return Disabled<HelloSignIn<TProfile>>();
        }

        var ticket = ReadTicket(command.RegistrationToken);
        if (ticket is null)
        {
            return InvalidTicket();
        }

        var consent = consentPolicy.Validate(new HelloRegistrationConsent(
            command.AcceptTermsOfService,
            command.AcceptPrivacyPolicy,
            command.AcceptTermsOfService || command.AcceptPrivacyPolicy
                ? timeProvider.GetUtcNow()
                : null));
        if (!consent.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                consent.Errors);
        }

        if (registrationAdmission is not null)
        {
            var admitted = await registrationAdmission.CheckAsync(
                HelloRegistrationKind.PhoneOtp,
                cancellationToken);
            if (!admitted.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                    admitted.Errors);
            }
        }

        var profile = command.Profile;
        if (profileEnricher is not null)
        {
            var enriched = profileEnricher.Enrich(profile, consent.Value);
            if (!enriched.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                    enriched.Errors);
            }

            profile = enriched.Value;
        }

        var created = await registration.RegisterVerifiedPhoneAsync(
            new RegisterVerifiedPhoneUserCommand<TProfile>(
                new CreateUserCommand<TProfile>(
                    null, null, ticket.Phone, profile),
                new VerificationProof(
                    ticket.ChallengeId,
                    ticket.Proof,
                    ticket.ExpiresAt)),
            cancellationToken);
        if (!created.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                created.Errors);
        }

        var issuedSession = await sessions.CreateAsync(
            new CreateIdentitySessionCommand(
                created.Value.Id,
                created.Value.SecurityStamp,
                command.SessionMetadata),
            cancellationToken);
        return issuedSession.IsSuccess
            ? OperationResultFactory.Success(
                new HelloSignIn<TProfile>(
                    ToAccount(created.Value),
                    ToSession(issuedSession.Value)))
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                issuedSession.Errors);
    }

    private RegistrationTicket? ReadTicket(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)
            || protectedToken.Length > 4096)
        {
            return null;
        }

        try
        {
            var ticket = JsonSerializer.Deserialize<RegistrationTicket>(
                protector.Unprotect(protectedToken));
            return ticket is not null
                && ticket.ExpiresAt > timeProvider.GetUtcNow()
                ? ticket
                : null;
        }
        catch (Exception exception) when (
            exception is System.Security.Cryptography.CryptographicException
                or JsonException
                or FormatException
                or ArgumentException)
        {
            return null;
        }
    }

    private static OperationResult<HelloSignIn<TProfile>> InvalidTicket()
        => OperationResultFactory.Fail<HelloSignIn<TProfile>>(
            new Error(
                IdentityErrorCodes.VerificationProofInvalid,
                "Registration token is invalid or expired.",
                ErrorType.Unauthorized));

    private static OperationResult<T> Disabled<T>()
        => OperationResultFactory.Fail<T>(
            HelloRegistrationErrors.Disabled());

    private static HelloAccount<TProfile> ToAccount(
        IdentityUser<TProfile> user)
        => new(user.Id, user.Flags, user.UserName, user.Email,
            user.EmailConfirmed, user.Phone, user.PhoneConfirmed,
            user.Profile, user.Version, user.CreatedAt, user.ModifiedAt);

    private static HelloSession ToSession(IssuedIdentitySession session)
        => new(session.SessionId, session.AccessToken,
            session.AccessTokenExpiresAt, session.RefreshToken,
            session.RefreshTokenExpiresAt);

    private sealed record RegistrationTicket(
        Guid ChallengeId,
        string Phone,
        string Proof,
        DateTimeOffset ExpiresAt);
}
