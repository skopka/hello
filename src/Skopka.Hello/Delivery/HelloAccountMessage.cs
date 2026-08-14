namespace Skopka.Hello;

public enum HelloDeliveryChannel
{
    Email = 0,
    Sms = 1,
    Authenticator = 2,
}

public enum HelloAccountMessageKind
{
    PasswordReset = 0,
    EmailConfirmation = 1,
    PhoneConfirmation = 2,
    PasswordChangeVerification = 3,
    ExternalLoginLinkVerification = 4,
    ExternalLoginUnlinkVerification = 5,

    AccountSecurityVerification = 6,

    AdminActionVerification = 7,
}

public sealed record HelloAccountMessage(
    Guid MessageId,
    HelloAccountMessageKind Kind,
    HelloDeliveryChannel Channel,
    string RecipientAddress,
    Uri? ActionUrl,
    DateTimeOffset ExpiresAt,
    string? VerificationCode = null,
    string? TemplateVariant = null);
