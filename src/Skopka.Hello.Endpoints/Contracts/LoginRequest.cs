namespace Skopka.Hello.Endpoints;

public sealed record LoginRequest(
    string Login,
    string Password);
