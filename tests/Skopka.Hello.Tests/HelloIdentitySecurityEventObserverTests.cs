using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.SecurityEvents;

namespace Skopka.Hello.Tests;

public sealed class HelloIdentitySecurityEventObserverTests
{
    [Fact]
    public void OnEventEnrichesSafeRequestContext()
    {
        var actorId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-123",
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim("sub", actorId.ToString("N")),
                ])),
        };
        var sink = new CapturingSink();
        var observer = new HelloIdentitySecurityEventObserver(
            new HttpContextAccessor
            {
                HttpContext = httpContext,
            },
            sink);
        var securityEvent = new IdentitySecurityEvent(
            Guid.NewGuid(),
            IdentitySecurityEventTypes.PasswordChanged,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        observer.OnEvent(securityEvent);

        Assert.NotNull(sink.Value);
        Assert.Equal(actorId, sink.Value.ActorUserId);
        Assert.Equal("trace-123", sink.Value.CorrelationId);
        Assert.Empty(sink.Value.Metadata);
    }

    private sealed class CapturingSink : IHelloSecurityEventSink
    {
        public HelloSecurityEventEnvelope? Value { get; private set; }

        public OperationResult Write(
            HelloSecurityEventEnvelope securityEvent)
        {
            Value = securityEvent;
            return OperationResultFactory.Success();
        }
    }
}
