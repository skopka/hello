namespace Skopka.Hello.Endpoints;

public sealed record ExternalLinkRequest(string ReturnUrl);

public sealed record ExternalLinkStartResponse(string ChallengeUrl);

public sealed record ExternalLoginMutationRequest(
    string VerificationCode);
