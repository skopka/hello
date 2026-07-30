using Microsoft.AspNetCore.Http;
using Skopka.Identity.Sessions;

namespace Skopka.Hello;

public interface IHelloRequestContext
{
    string? CreateClientKey(HttpContext httpContext);

    IdentitySessionMetadata CreateSessionMetadata(
        HttpContext httpContext,
        string clientName);
}
