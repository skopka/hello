namespace Skopka.Hello;

public enum HelloAccountMessageKind
{
    PasswordReset = 0,
    EmailConfirmation = 1,
    StepUpVerification = 2,
}

public sealed record HelloAccountMessage(
    HelloAccountMessageKind Kind,
    string RecipientAddress,
    Uri? ActionUrl,
    DateTimeOffset ExpiresAt,
    string? VerificationCode = null);
