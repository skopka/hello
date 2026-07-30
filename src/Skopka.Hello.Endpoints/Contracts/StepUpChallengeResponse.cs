namespace Skopka.Hello.Endpoints;

public sealed record StepUpChallengeResponse(
    Guid ChallengeId,
    DateTimeOffset ExpiresAt);
