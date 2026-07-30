using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skopka.Identity.SecurityEvents;

namespace Skopka.Hello;

internal sealed class HelloIdentitySecurityEventObserver(
    IHttpContextAccessor httpContextAccessor,
    IHelloSecurityEventSink sink)
    : IIdentitySecurityEventObserver
{
    public void OnEvent(IdentitySecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);

        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            _ = sink.Write(
                new HelloSecurityEventEnvelope(
                    securityEvent.EventId,
                    securityEvent.Type,
                    securityEvent.UserId,
                    ReadActorId(httpContext?.User),
                    securityEvent.ResourceId,
                    httpContext?.TraceIdentifier,
                    securityEvent.OccurredAt,
                    new Dictionary<string, string>()));
        }
        catch
        {
            // Identity observers are post-commit and must never break the operation.
        }
    }

    private static Guid? ReadActorId(ClaimsPrincipal? principal)
    {
        var subject = principal?.FindFirstValue("sub")
            ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out var actorId)
            ? actorId
            : null;
    }
}
