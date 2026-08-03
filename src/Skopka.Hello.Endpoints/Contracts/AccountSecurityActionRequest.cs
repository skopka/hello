namespace Skopka.Hello.Endpoints;

public sealed record SetPasswordRequest(
    Guid ChallengeId,
    string VerificationCode,
    string NewPassword);

public sealed record CompleteAccountSecurityActionRequest(
    Guid ChallengeId,
    string VerificationCode);
