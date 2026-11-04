using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Tests;

public sealed class HelloEmailWorkerTests
{
    [Theory]
    [InlineData(HelloEmailTransportErrorCodes.Permanent, 1)]
    [InlineData(HelloEmailTransportErrorCodes.Retryable, 3)]
    [InlineData("sensitive-provider-code", 3)]
    [InlineData("throw", 3)]
    public async Task WorkerRetriesOnlyRetryableFailuresAndNeverLogsProviderDetails(
        string code, int expectedAttempts)
    {
        var logger = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new FailureState(code));
        services.AddSkopkaHelloEmailTransport<FailingTransport>();
        services.AddSkopkaHelloSmtpProvider(options =>
        {
            options.Host = "smtp.example.test";
            options.FromAddress = "sender@example.test";
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var worker = new SmtpHelloAccountMessageWorker(
            provider.GetRequiredService<SmtpHelloAccountMessageQueue>(),
            provider.GetRequiredService<SmtpHelloAccountMessageTransport>(),
            provider.GetRequiredService<HelloSmtpOptions>(), logger);
        var sender = provider.GetServices<IHelloAccountMessageProvider>().Single();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var message = new HelloAccountMessage(
                Guid.NewGuid(), HelloAccountMessageKind.PasswordReset,
                HelloDeliveryChannel.Email, "alice@example.test",
                new Uri("https://accounts.example.test/?token=sensitive-token"),
                DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.True((await sender.SendAsync(message, CancellationToken.None)).IsSuccess);
            Assert.True((await sender.SendAsync(message with
            {
                MessageId = Guid.NewGuid(),
                RecipientAddress = "marker@example.test",
            }, CancellationToken.None)).IsSuccess);
            await provider.GetRequiredService<FailureState>().Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }

        Assert.Equal(expectedAttempts, provider.GetRequiredService<FailureState>().Attempts);
        Assert.Equal(expectedAttempts, logger.Messages.Count);
        foreach (var message in logger.Messages)
        {
            Assert.DoesNotContain("sensitive-provider-code", message, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive-provider-message", message, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive-token", message, StringComparison.Ordinal);
            Assert.DoesNotContain("alice@example.test", message, StringComparison.Ordinal);
        }
    }

    public sealed class FailureState(string code)
    {
        public string Code { get; } = code;
        private int attempts;
        public int Attempts => attempts;
        public void RecordAttempt() => Interlocked.Increment(ref attempts);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class FailingTransport(FailureState state) : IHelloEmailTransport
    {
        public Task<OperationResult> SendAsync(HelloEmail email, CancellationToken cancellationToken)
        {
            if (email.RecipientAddress == "marker@example.test")
            {
                state.Completed.TrySetResult();
                return Task.FromResult(OperationResultFactory.Success());
            }
            state.RecordAttempt();
            if (state.Code == "throw")
                throw new InvalidOperationException("sensitive-provider-message sensitive-token");
            return Task.FromResult(OperationResultFactory.Fail(
                new Error(state.Code, "sensitive-provider-message", ErrorType.Failure)));
        }
    }

    private sealed class RecordingLogger : ILogger<SmtpHelloAccountMessageWorker>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
