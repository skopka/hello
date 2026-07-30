namespace Skopka.Hello.Endpoints;

public sealed record ConfirmEmailRequest(
    Guid UserId,
    string Email,
    string Token);
