using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public void OnEventLogsSafeErrorWhenSinkRejectsEvent()
    {
        var logger = new RecordingLogger<
            HelloIdentitySecurityEventObserver>();
        var observer = new HelloIdentitySecurityEventObserver(
            new HttpContextAccessor(),
            new RejectingSink(),
            logger);

        observer.OnEvent(
            new IdentitySecurityEvent(
                Guid.NewGuid(),
                IdentitySecurityEventTypes.PasswordChanged,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()));

        Assert.Contains(
            logger.Events,
            eventId => eventId.Id == 2002);
    }

    [Fact]
    public void OnEventLogsAndSuppressesSinkException()
    {
        var logger = new RecordingLogger<
            HelloIdentitySecurityEventObserver>();
        var observer = new HelloIdentitySecurityEventObserver(
            new HttpContextAccessor(),
            new ThrowingSink(),
            logger);

        observer.OnEvent(
            new IdentitySecurityEvent(
                Guid.NewGuid(),
                IdentitySecurityEventTypes.PasswordChanged,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()));

        Assert.Contains(
            logger.Events,
            eventId => eventId.Id == 2002);
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

    private sealed class RejectingSink : IHelloSecurityEventSink
    {
        public OperationResult Write(
            HelloSecurityEventEnvelope securityEvent)
            => OperationResultFactory.Fail(
                new Error(
                    HelloAuditErrorCodes.Failed,
                    "Audit failed.",
                    ErrorType.Failure));
    }

    private sealed class ThrowingSink : IHelloSecurityEventSink
    {
        public OperationResult Write(
            HelloSecurityEventEnvelope securityEvent)
            => throw new InvalidOperationException("Sink failed.");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<EventId> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Events.Add(eventId);
    }
}
