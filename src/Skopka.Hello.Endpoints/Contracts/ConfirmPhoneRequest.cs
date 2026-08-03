namespace Skopka.Hello.Endpoints;

public sealed record ConfirmPhoneRequest(
    Guid UserId,
    string Phone,
    string Token);
