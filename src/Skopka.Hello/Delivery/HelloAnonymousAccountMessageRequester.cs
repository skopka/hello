using System.ComponentModel.DataAnnotations;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity;
using Skopka.Identity.Authentication;
using Skopka.Identity.Errors;
using Skopka.Identity.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Skopka.Hello;

internal sealed class HelloAnonymousAccountMessageRequester<TProfile>
{
    private const string ClientScope =
        "hello.account-message.client";
    private const string UnavailableClientKey = "unavailable";
    private const int MaximumEmailLength = 320;

    private readonly IIdentityNormalizer normalizer;
    private readonly IdentityRateLimitOptions rateLimitOptions;
    private readonly IIdentityRateLimiter<TProfile>? rateLimiter;
    private readonly IHelloAnonymousAccountMessageInbox queue;
    private readonly ILogger<HelloAnonymousAccountMessageRequester<TProfile>>?
        logger;
    private static readonly Action<ILogger, HelloAccountMessageKind,
        Guid, string, Exception?> LogInboxRejected = LoggerMessage.Define<
            HelloAccountMessageKind,
            Guid,
            string>(
                LogLevel.Warning,
                new EventId(2001, "AnonymousAccountMessageInboxRejected"),
                "Dropped anonymous {MessageKind} account-message request {MessageId}; inbox error code: {ErrorCode}.");

    public HelloAnonymousAccountMessageRequester(
        IIdentityNormalizer normalizer,
        IdentityRateLimitOptions rateLimitOptions,
        IEnumerable<IIdentityRateLimiter<TProfile>> rateLimiters,
        IHelloAnonymousAccountMessageInbox queue,
        ILogger<HelloAnonymousAccountMessageRequester<TProfile>>? logger =
            null)
    {
        this.normalizer = normalizer;
        this.rateLimitOptions = rateLimitOptions;
        rateLimiter = rateLimiters.FirstOrDefault();
        this.queue = queue;
        this.logger = logger;
    }

    public async Task<OperationResult> EnqueueAsync(
        HelloAccountMessageKind kind,
        string target,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedTarget = NormalizeTarget(kind, target);
        if (!normalizedTarget.IsSuccess)
        {
            return OperationResultFactory.Fail(
                normalizedTarget.Errors);
        }

        if (rateLimiter is not null)
        {
            var clientDecision = await rateLimiter.HitAsync(
                new RateLimitRequest(
                    ClientScope,
                    NormalizeClientKey(clientKey),
                    rateLimitOptions.VerificationClientPermitLimit,
                    rateLimitOptions.VerificationClientWindow),
                cancellationToken);
            if (!clientDecision.IsAllowed)
            {
                return OperationResultFactory.Success();
            }

            var targetDecision = await rateLimiter.HitAsync(
                new RateLimitRequest(
                    GetTargetScope(kind),
                    normalizedTarget.Value,
                    rateLimitOptions.VerificationIntentPermitLimit,
                    rateLimitOptions.VerificationIntentWindow,
                    rateLimitOptions.VerificationResendCooldown),
                cancellationToken);
            if (!targetDecision.IsAllowed)
            {
                return OperationResultFactory.Success();
            }
        }

        var request = new HelloAnonymousAccountMessageRequest(
            Guid.NewGuid(),
            kind,
            normalizedTarget.Value);
        var queued = await queue.EnqueueAsync(
            request,
            cancellationToken);
        if (!queued.IsSuccess)
        {
            HelloDiagnostics.AnonymousQueueDrops.Add(
                1,
                new KeyValuePair<string, object?>(
                    "message.kind",
                    kind.ToString()));
            if (logger is not null)
            {
                LogInboxRejected(
                    logger,
                    kind,
                    request.MessageId,
                    queued.Errors.FirstOrDefault()?.Code
                        ?? HelloDeliveryErrorCodes.Failed,
                    null);
            }
        }

        return OperationResultFactory.Success();
    }

    private OperationResult<string> NormalizeTarget(
        HelloAccountMessageKind kind,
        string target)
    {
        string? candidate;
        string? normalized;
        Error? validation;
        switch (kind)
        {
            case HelloAccountMessageKind.PasswordReset:
            case HelloAccountMessageKind.EmailConfirmation:
                candidate = TrimIfBounded(
                    target,
                    MaximumEmailLength);
                validation = ValidateEmail(candidate);
                normalized = validation is null
                    ? normalizer.NormalizeEmail(candidate)
                    : null;
                break;
            case HelloAccountMessageKind.PhoneConfirmation:
                candidate = TrimIfBounded(
                    target,
                    IdentityLoginLimits.MaximumLoginLength);
                validation = ValidatePhone(candidate);
                normalized = validation is null
                    ? normalizer.NormalizePhoneLoginIdentifier(candidate)
                    : null;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Only anonymous action-message kinds can be queued.");
        }

        if (validation is not null)
        {
            return OperationResultFactory.Fail<string>(validation);
        }

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > IdentityLoginLimits.MaximumLoginLength)
        {
            var field = kind == HelloAccountMessageKind.PhoneConfirmation
                ? "phone"
                : "email";
            return OperationResultFactory.Fail<string>(
                ValidationError(
                    field,
                    field == "phone"
                        ? "Enter a valid phone number."
                        : "Enter a valid email address."));
        }

        return OperationResultFactory.Success(normalized);
    }

    private static string? TrimIfBounded(
        string? value,
        int maximumLength)
        => value is null || value.Length > maximumLength
            ? null
            : value.Trim();

    private static Error? ValidateEmail(string? email)
        => string.IsNullOrWhiteSpace(email)
            || email.Length > MaximumEmailLength
            || !new EmailAddressAttribute().IsValid(email)
                ? ValidationError(
                    "email",
                    "Enter a valid email address.")
                : null;

    private static Error? ValidatePhone(string? phone)
        => string.IsNullOrWhiteSpace(phone)
            || phone.Length > IdentityLoginLimits.MaximumLoginLength
            ? ValidationError(
                "phone",
                "Enter a valid phone number.")
            : null;

    private static string NormalizeClientKey(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return UnavailableClientKey;
        }

        return normalized.Length <= RateLimitLimits.MaximumClientKeyLength
            ? normalized
            : normalized[..RateLimitLimits.MaximumClientKeyLength];
    }

    private static string GetTargetScope(
        HelloAccountMessageKind kind)
        => kind switch
        {
            HelloAccountMessageKind.PasswordReset =>
                "hello.account-message.target.password-reset",
            HelloAccountMessageKind.EmailConfirmation =>
                "hello.account-message.target.email-confirmation",
            HelloAccountMessageKind.PhoneConfirmation =>
                "hello.account-message.target.phone-confirmation",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Only anonymous action-message kinds have target scopes."),
        };

    private static Error ValidationError(
        string field,
        string message)
        => new(
            IdentityErrorCodes.Validation,
            "Validation failed.",
            ErrorType.Validation,
            new ValidationDetails(
                new Dictionary<string, string[]>
                {
                    [field] = [message],
                }));
}
