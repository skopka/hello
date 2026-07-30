using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public interface IHelloSessionCookieManager
{
    OperationResult ValidateTransport(HttpContext httpContext);

    Task<OperationResult> ValidateAntiforgeryAsync(
        HttpContext httpContext);

    string? ReadRefreshToken(HttpContext httpContext);

    void WriteSessionCookies(
        HttpContext httpContext,
        HelloSession session);

    void DeleteSessionCookies(HttpContext httpContext);
}
