using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.StepUp;
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
        => CreateDeliveryBinding(
            userId,
            channel,
            destination,
            PasswordChangeAction);

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
            _ => throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "The verification delivery channel is unsupported."),
        };
        return destination is not null;
    }
}

internal sealed class HelloStepUpPolicyProvider<TProfile>
    : IStepUpPolicyProvider<TProfile>
{
    private static readonly StepUpRequirement PasswordChange =
        new(
            HelloAccountSecurity.PasswordChangePurpose,
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
            HelloAccountSecurity.ExternalLinkAction => ExternalLink,
            HelloAccountSecurity.ExternalUnlinkAction => ExternalUnlink,
            _ => null,
        };
        return Task.FromResult(requirement);
    }
}
