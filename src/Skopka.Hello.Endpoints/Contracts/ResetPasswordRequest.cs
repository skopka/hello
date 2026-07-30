namespace Skopka.Hello.Endpoints;

public sealed record ResetPasswordRequest(
    Guid UserId,
    string Token,
    string NewPassword);
