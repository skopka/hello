using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal static class HelloAccountMessageValidator
{
    public static Error? Validate(
        HelloAccountMessage message,
        DateTimeOffset now)
    {
        if (message.MessageId == Guid.Empty
            || string.IsNullOrWhiteSpace(message.RecipientAddress))
        {
            return Invalid();
        }

        if (message.Channel is not HelloDeliveryChannel.Email
            and not HelloDeliveryChannel.Sms
            || !Enum.IsDefined(message.Kind))
        {
            return Invalid();
        }

        if (message.ExpiresAt <= now)
        {
            return new Error(
                HelloDeliveryErrorCodes.Expired,
                "The account message has expired.",
                ErrorType.Failure);
        }

        if (message.TemplateVariant is { } templateVariant
            && (string.IsNullOrWhiteSpace(templateVariant)
                || templateVariant.Length > 128
                || templateVariant.Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not '.' and not '-' and not '_')))
        {
            return Invalid();
        }

        var isActionMessage = message.Kind is
            HelloAccountMessageKind.PasswordReset
            or HelloAccountMessageKind.EmailConfirmation
            or HelloAccountMessageKind.PhoneConfirmation;
        if (isActionMessage
            && (message.ActionUrl is null
                || !message.ActionUrl.IsAbsoluteUri
                || (message.ActionUrl.Scheme != Uri.UriSchemeHttps
                    && message.ActionUrl.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrWhiteSpace(
                    message.VerificationCode)))
        {
            return Invalid();
        }

        var isVerificationMessage = message.Kind is
            HelloAccountMessageKind.PasswordChangeVerification
            or HelloAccountMessageKind.ExternalLoginLinkVerification
            or HelloAccountMessageKind.ExternalLoginUnlinkVerification
            or HelloAccountMessageKind.AccountSecurityVerification
            or HelloAccountMessageKind.AdminActionVerification;
        if (isVerificationMessage
            && (message.ActionUrl is not null
                || string.IsNullOrWhiteSpace(
                    message.VerificationCode)))
        {
            return Invalid();
        }

        if (!isActionMessage && !isVerificationMessage)
        {
            return Invalid();
        }

        if (message.Kind == HelloAccountMessageKind.EmailConfirmation
            && message.Channel != HelloDeliveryChannel.Email)
        {
            return ChannelMismatch();
        }

        if (message.Kind == HelloAccountMessageKind.PhoneConfirmation
            && message.Channel != HelloDeliveryChannel.Sms)
        {
            return ChannelMismatch();
        }

        return null;
    }

    public static Error ChannelMismatch()
        => new(
            HelloDeliveryErrorCodes.ChannelMismatch,
            "The account message does not match the delivery channel.",
            ErrorType.Failure);

    private static Error Invalid()
        => new(
            HelloDeliveryErrorCodes.InvalidMessage,
            "The account message is invalid.",
            ErrorType.Failure);
}
