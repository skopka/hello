namespace Skopka.Hello.Endpoints;

public sealed record LoginRequest(
    string Handle,
    string Login,
    string Password);
