using Skopka.Identity;
using Skopka.Identity.Errors;
using Skopka.Identity.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Skopka.Hello.Tests;

public sealed class HelloAnonymousAccountMessageRequesterTests
{
    [Fact]
    public void QueueCapacityMustBeBounded()
    {
        var options = new HelloDeliveryOptions
        {
            AnonymousRequestQueueCapacity = 0,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public async Task ValidRequestUsesTrustedClientAndTargetPartitions()
    {
        var limiter = new RecordingRateLimiter();
        var options = CreateRateLimitOptions();
        var queue = CreateQueue();
        var requester = CreateRequester(
            queue,
            options,
            limiter);

        var result = await requester.EnqueueAsync(
            HelloAccountMessageKind.EmailConfirmation,
            " Alice@Example.Test ",
            " 192.0.2.10 ",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            limiter.Hits,
            client =>
            {
                Assert.Equal(
                    "hello.account-message.client",
                    client.Scope);
                Assert.Equal("192.0.2.10", client.Key);
                Assert.Equal(
                    options.VerificationClientPermitLimit,
                    client.PermitLimit);
                Assert.Equal(
                    options.VerificationClientWindow,
                    client.Window);
                Assert.Null(client.MinimumInterval);
            },
            target =>
            {
                Assert.Equal(
                    "hello.account-message.target.email-confirmation",
                    target.Scope);
                Assert.Equal(
                    "ALICE@EXAMPLE.TEST",
                    target.Key);
                Assert.Equal(
                    options.VerificationIntentPermitLimit,
                    target.PermitLimit);
                Assert.Equal(
                    options.VerificationIntentWindow,
                    target.Window);
                Assert.Equal(
                    options.VerificationResendCooldown,
                    target.MinimumInterval);
            });

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(1));
        await using var reader = queue
            .ReadAllAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.NotEqual(Guid.Empty, reader.Current.Request.MessageId);
        Assert.Equal(
            HelloAccountMessageKind.EmailConfirmation,
            reader.Current.Request.Kind);
        Assert.Equal(
            "ALICE@EXAMPLE.TEST",
            reader.Current.Request.NormalizedTarget);
    }

    [Fact]
    public async Task InvalidTargetDoesNotConsumeLimitsOrQueueCapacity()
    {
        var limiter = new RecordingRateLimiter();
        var queue = CreateQueue(capacity: 1);
        var requester = CreateRequester(
            queue,
            CreateRateLimitOptions(),
            limiter);

        var result = await requester.EnqueueAsync(
            HelloAccountMessageKind.PhoneConfirmation,
            "not-a-phone",
            "192.0.2.11",
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code == IdentityErrorCodes.Validation);
        Assert.Empty(limiter.Hits);
        Assert.True(
            (await queue.EnqueueAsync(
                new HelloAnonymousAccountMessageRequest(
                    Guid.NewGuid(),
                    HelloAccountMessageKind.PhoneConfirmation,
                    "15551234567"),
                CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task TargetCooldownIsSilentlyDroppedBeforeEnqueue()
    {
        var retryAfter = DateTimeOffset.UtcNow.AddSeconds(20);
        var limiter = new RecordingRateLimiter(
            new RateLimitDecision(true, null),
            new RateLimitDecision(false, retryAfter));
        var queue = CreateQueue(capacity: 1);
        var requester = CreateRequester(
            queue,
            CreateRateLimitOptions(),
            limiter);

        var result = await requester.EnqueueAsync(
            HelloAccountMessageKind.PasswordReset,
            "alice@example.test",
            null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("unavailable", limiter.Hits[0].Key);
        Assert.True(
            (await queue.EnqueueAsync(
                new HelloAnonymousAccountMessageRequest(
                    Guid.NewGuid(),
                    HelloAccountMessageKind.PasswordReset,
                    "ALICE@EXAMPLE.TEST"),
                CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task FullQueueIsSilentlyDropped()
    {
        var limiter = new RecordingRateLimiter();
        var logger = new RecordingLogger<
            HelloAnonymousAccountMessageRequester<object>>();
        var queue = CreateQueue(capacity: 1);
        Assert.True(
            (await queue.EnqueueAsync(
                new HelloAnonymousAccountMessageRequest(
                    Guid.NewGuid(),
                    HelloAccountMessageKind.PasswordReset,
                    "OTHER@EXAMPLE.TEST"),
                CancellationToken.None)).IsSuccess);
        var requester = CreateRequester(
            queue,
            CreateRateLimitOptions(),
            limiter,
            logger);

        var result = await requester.EnqueueAsync(
            HelloAccountMessageKind.PasswordReset,
            "alice@example.test",
            "192.0.2.12",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, limiter.Hits.Count);
        Assert.Contains(
            logger.Events,
            eventId => eventId.Id == 2001);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(1));
        await using var reader = queue
            .ReadAllAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(
            "OTHER@EXAMPLE.TEST",
            reader.Current.Request.NormalizedTarget);
    }

    private static HelloAnonymousAccountMessageRequester<object>
        CreateRequester(
            IHelloAnonymousAccountMessageInbox queue,
            IdentityRateLimitOptions options,
            IIdentityRateLimiter<object> limiter,
            ILogger<HelloAnonymousAccountMessageRequester<object>>? logger =
                null)
        => new(
            new DefaultIdentityNormalizer(),
            options,
            [limiter],
            queue,
            logger);

    private static InMemoryHelloAnonymousAccountMessageInbox CreateQueue(
        int capacity = 8)
        => new InMemoryHelloAnonymousAccountMessageInbox(
            new HelloDeliveryOptions
            {
                AnonymousRequestQueueCapacity = capacity,
            });

    private static IdentityRateLimitOptions CreateRateLimitOptions()
        => new()
        {
            VerificationClientPermitLimit = 7,
            VerificationClientWindow = TimeSpan.FromMinutes(2),
            VerificationIntentPermitLimit = 3,
            VerificationIntentWindow = TimeSpan.FromMinutes(9),
            VerificationResendCooldown = TimeSpan.FromSeconds(45),
        };

    private sealed class RecordingRateLimiter(
        params RateLimitDecision[] scriptedDecisions)
        : IIdentityRateLimiter<object>
    {
        private readonly Queue<RateLimitDecision> decisions =
            new(scriptedDecisions);

        public List<RateLimitRequest> Hits { get; } = [];

        public Task<RateLimitDecision> CheckAsync(
            RateLimitRequest request,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RateLimitDecision> HitAsync(
            RateLimitRequest request,
            CancellationToken ct)
        {
            Hits.Add(request);
            return Task.FromResult(
                decisions.Count > 0
                    ? decisions.Dequeue()
                    : new RateLimitDecision(true, null));
        }

        public Task ResetAsync(
            string scope,
            string key,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> PruneAsync(CancellationToken ct)
            => throw new NotSupportedException();
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
