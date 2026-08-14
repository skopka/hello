using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.StepUp;
using Skopka.Identity.Totp;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal static class HelloAccountSecurity
{
    private const string DeliveryBindingVersion =
        "hello-delivery-binding:v2";

    private const string DestinationFingerprintVersion =
        "hello-delivery-destination:v1";

    public const string PasswordChangeAction =
        "account.password.change";

    public const string PasswordChangePurpose =
        "hello:account.password.change";

    public const string PasswordSetAction =
        "account.password.set";

    public const string PasswordSetPurpose =
        "hello:account.password.set";

    public const string PasswordRemoveAction =
        "account.password.remove";

    public const string PasswordRemovePurpose =
        "hello:account.password.remove";

    public const string AccountDeleteAction =
        "account.delete";

    public const string AccountDeletePurpose =
        "hello:account.delete";

    public const string AuthenticatorDisableAction =
        "account.authenticator.disable";

    public const string AuthenticatorDisablePurpose =
        "hello:account.authenticator.disable";

    public const string ExternalLinkAction =
        "account.external.link";

    public const string ExternalLinkPurpose =
        "hello:account.external.link";

    public const string ExternalUnlinkAction =
        "account.external.unlink";

    public const string ExternalUnlinkPurpose =
        "hello:account.external.unlink";

    public static string CreateBinding(
        Guid userId,
        HelloDeliveryChannel channel,
        string destination)
        => CreateBinding(
            userId,
            channel,
            destination,
            PasswordChangeAction);

    public static string CreateBinding(
        Guid userId,
        HelloDeliveryChannel channel,
        string destination,
        string action)
        => CreateDeliveryBinding(
            userId,
            channel,
            destination,
            action);

    public static string CreateBinding(
        Guid userId,
        HelloStepUpMethodSelection selection,
        string action)
        => CreateDeliveryBinding(
            userId,
            selection.Channel,
            selection.Destination ?? "totp",
            action);

    public static string CreateExternalLoginBinding(
        ExternalLoginKey login,
        Guid userId,
        HelloDeliveryChannel channel,
        string destination)
    {
        ArgumentNullException.ThrowIfNull(login);
        var provider = Encoding.UTF8.GetBytes(login.Provider);
        var subject = Encoding.UTF8.GetBytes(login.Subject);
        var payload = new byte[8 + provider.Length + subject.Length];
        BinaryPrimitives.WriteInt32BigEndian(
            payload.AsSpan(0, 4),
            provider.Length);
        provider.CopyTo(payload.AsSpan(4));
        var subjectLengthOffset = 4 + provider.Length;
        BinaryPrimitives.WriteInt32BigEndian(
            payload.AsSpan(subjectLengthOffset, 4),
            subject.Length);
        subject.CopyTo(payload.AsSpan(subjectLengthOffset + 4));

        var resourceBinding = Convert.ToHexString(
            SHA256.HashData(payload));
        return CreateDeliveryBinding(
            userId,
            channel,
            destination,
            resourceBinding);
    }

    public static string CreateExternalLoginBinding(
        ExternalLoginKey login,
        Guid userId,
        HelloStepUpMethodSelection selection)
        => CreateExternalLoginBinding(
            login,
            userId,
            selection.Channel,
            selection.Destination ?? "totp");

    private static string CreateDeliveryBinding(
        Guid userId,
        HelloDeliveryChannel channel,
        string destination,
        string resourceBinding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceBinding);

        var destinationFingerprint = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"{DestinationFingerprintVersion}|"
                    + $"{(int)channel}|{destination}")));
        var value = $"{DeliveryBindingVersion}|{userId:D}|"
            + $"{(int)channel}|{destinationFingerprint}|"
            + $"{resourceBinding.Length}:{resourceBinding}";
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static Error ConfirmedEmailRequired()
        => new(
            "hello.account.confirmed_email_required",
            "A confirmed email address is required for this action.",
            ErrorType.Forbidden);

    public static Error ConfirmedPhoneRequired()
        => new(
            "hello.account.confirmed_phone_required",
            "A confirmed phone number is required for this action.",
            ErrorType.Forbidden);

    public static Error ConfirmedDestinationRequired(
        HelloDeliveryChannel channel)
        => channel switch
        {
            HelloDeliveryChannel.Email => ConfirmedEmailRequired(),
            HelloDeliveryChannel.Sms => ConfirmedPhoneRequired(),
            HelloDeliveryChannel.Authenticator => new Error(
                IdentityErrorCodes.TotpNotEnabled,
                "An authenticator is required for this action.",
                ErrorType.Forbidden),
            _ => throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "The verification delivery channel is unsupported."),
        };

    public static Error ConcurrencyConflict()
        => new(
            IdentityErrorCodes.ConcurrencyConflict,
            "Concurrency conflict.",
            ErrorType.Conflict);

    public static bool IsRetryableVerificationResponse(
        IReadOnlyCollection<Error> errors)
        => errors.Count == 1
            && string.Equals(
                errors.First().Code,
                IdentityErrorCodes.VerificationResponseInvalid,
                StringComparison.Ordinal);

    public static bool HasConfirmedEmail<TProfile>(
        IdentityUser<TProfile> user)
        => user.EmailConfirmed
            && !string.IsNullOrWhiteSpace(user.Email);

    public static bool TryGetConfirmedDestination<TProfile>(
        IdentityUser<TProfile> user,
        HelloDeliveryChannel channel,
        out string? destination)
    {
        ArgumentNullException.ThrowIfNull(user);

        destination = channel switch
        {
            HelloDeliveryChannel.Email
                when HasConfirmedEmail(user) => user.Email,
            HelloDeliveryChannel.Sms
                when user.PhoneConfirmed
                    && !string.IsNullOrWhiteSpace(user.Phone) =>
                user.Phone,
            HelloDeliveryChannel.Email or HelloDeliveryChannel.Sms =>
                null,
            HelloDeliveryChannel.Authenticator => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "The verification delivery channel is unsupported."),
        };
        return destination is not null;
    }
}

internal sealed record HelloStepUpMethodSelection(
    string Method,
    HelloDeliveryChannel Channel,
    string? Destination);

internal sealed class HelloStepUpMethodResolver<TProfile>(
    HelloDeliveryOptions options,
    IHelloAccountMessageSender messageSender,
    IIdentityTotpService<TProfile>? totp = null)
{
    public async Task<OperationResult<string>> GetRequiredMethodAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!options.RequireTotpWhenEnabled)
        {
            return OperationResultFactory.Success(
                VerificationMethods.OneTimeCode);
        }

        if (totp is null)
        {
            return OperationResultFactory.Fail<string>(
                new Error(
                    IdentityErrorCodes.VerificationMethodUnavailable,
                    "TOTP is required by Hello but is not configured in Skopka.Identity.",
                    ErrorType.Failure));
        }

        var status = await totp.GetStatusAsync(userId, cancellationToken);
        if (!status.IsSuccess)
        {
            return OperationResultFactory.Fail<string>(status.Errors);
        }

        return OperationResultFactory.Success(
            status.Value.IsEnabled
                ? VerificationMethods.TimeBasedOneTimePassword
                : VerificationMethods.OneTimeCode);
    }

    public async Task<OperationResult<HelloStepUpMethodSelection>> SelectAsync(
        IdentityUser<TProfile> user,
        CancellationToken cancellationToken)
    {
        var method = await GetRequiredMethodAsync(
            user.Id,
            cancellationToken);
        if (!method.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpMethodSelection>(
                method.Errors);
        }

        return Resolve(user, method.Value, requireDelivery: true);
    }

    public OperationResult<HelloStepUpMethodSelection> Resolve(
        IdentityUser<TProfile> user,
        string method,
        bool requireDelivery)
    {
        if (string.Equals(
                method,
                VerificationMethods.TimeBasedOneTimePassword,
                StringComparison.Ordinal))
        {
            return OperationResultFactory.Success(
                new HelloStepUpMethodSelection(
                    method,
                    HelloDeliveryChannel.Authenticator,
                    Destination: null));
        }

        if (!string.Equals(
                method,
                VerificationMethods.OneTimeCode,
                StringComparison.Ordinal))
        {
            return OperationResultFactory.Fail<HelloStepUpMethodSelection>(
                new Error(
                    IdentityErrorCodes.VerificationMethodUnavailable,
                    "The verification method is not supported by Hello.",
                    ErrorType.Validation));
        }

        if (!HelloAccountSecurity.TryGetConfirmedDestination(
                user,
                options.VerificationChannel,
                out var destination))
        {
            return OperationResultFactory.Fail<HelloStepUpMethodSelection>(
                HelloAccountSecurity.ConfirmedDestinationRequired(
                    options.VerificationChannel));
        }

        if (requireDelivery)
        {
            var available = messageSender.CheckAvailability(
                options.VerificationChannel);
            if (!available.IsSuccess)
            {
                return OperationResultFactory.Fail<
                    HelloStepUpMethodSelection>(available.Errors);
            }
        }

        return OperationResultFactory.Success(
            new HelloStepUpMethodSelection(
                method,
                options.VerificationChannel,
                destination));
    }
}

internal sealed class HelloAccountStepUpRequirementProvider<TProfile>
    : IHelloStepUpRequirementProvider<TProfile>
{
    private static readonly StepUpRequirement PasswordChange =
        new(
            HelloAccountSecurity.PasswordChangePurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    private static readonly StepUpRequirement PasswordSet =
        new(
            HelloAccountSecurity.PasswordSetPurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    private static readonly StepUpRequirement PasswordRemove =
        new(
            HelloAccountSecurity.PasswordRemovePurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    private static readonly StepUpRequirement AccountDelete =
        new(
            HelloAccountSecurity.AccountDeletePurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    private static readonly StepUpRequirement AuthenticatorDisable =
        new(
            HelloAccountSecurity.AuthenticatorDisablePurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    private static readonly StepUpRequirement ExternalLink =
        new(
            HelloAccountSecurity.ExternalLinkPurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    private static readonly StepUpRequirement ExternalUnlink =
        new(
            HelloAccountSecurity.ExternalUnlinkPurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    public Task<StepUpRequirement?> GetRequirementAsync(
        StepUpAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var requirement = context.Action switch
        {
            HelloAccountSecurity.PasswordChangeAction => PasswordChange,
            HelloAccountSecurity.PasswordSetAction => PasswordSet,
            HelloAccountSecurity.PasswordRemoveAction => PasswordRemove,
            HelloAccountSecurity.AccountDeleteAction => AccountDelete,
            HelloAccountSecurity.AuthenticatorDisableAction =>
                AuthenticatorDisable,
            HelloAccountSecurity.ExternalLinkAction => ExternalLink,
            HelloAccountSecurity.ExternalUnlinkAction => ExternalUnlink,
            _ => null,
        };
        return Task.FromResult(requirement);
    }
}

internal sealed class HelloStepUpPolicyProvider<TProfile>(
    IEnumerable<IHelloStepUpRequirementProvider<TProfile>> providers,
    HelloStepUpMethodResolver<TProfile>? methodResolver = null)
    : IStepUpPolicyProvider<TProfile>
{
    public HelloStepUpPolicyProvider()
        : this([new HelloAccountStepUpRequirementProvider<TProfile>()])
    {
    }

    public async Task<StepUpRequirement?> GetRequirementAsync(
        StepUpAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        StepUpRequirement? selected = null;
        foreach (var provider in providers)
        {
            var requirement = await provider.GetRequirementAsync(
                context,
                cancellationToken);
            if (requirement is null)
            {
                continue;
            }

            // Overlapping providers are a configuration error. Identity treats a
            // missing policy as a safe denial, so fail closed without throwing.
            if (selected is not null)
            {
                return null;
            }

            selected = requirement;
        }

        if (selected is null
            || methodResolver is null
            || !selected.AllowedMethods.Contains(
                VerificationMethods.OneTimeCode,
                StringComparer.Ordinal))
        {
            return selected;
        }

        var method = await methodResolver.GetRequiredMethodAsync(
            context.UserId,
            cancellationToken);
        return method.IsSuccess
            ? selected with
            {
                AllowedMethods = selected.AllowedMethods
                    .Select(item => string.Equals(
                            item,
                            VerificationMethods.OneTimeCode,
                            StringComparison.Ordinal)
                        ? method.Value
                        : item)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            }
            : null;
    }
}
