namespace Skopka.Hello.Endpoints;

public sealed record ChangePasswordRequest(
    Guid ChallengeId,
    string VerificationCode,
    string CurrentPassword,
    string NewPassword);
