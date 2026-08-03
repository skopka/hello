namespace Skopka.Hello.Tests;

public sealed class QueuedSmtpHelloAccountMessageProviderTests
{
    [Fact]
    public async Task SendQueuesMessageWithoutWaitingForTransport()
    {
        var options = new HelloSmtpOptions { QueueCapacity = 1 };
        var queue = new SmtpHelloAccountMessageQueue(options);
        var sender = new QueuedSmtpHelloAccountMessageProvider(
            options,
            queue);
        var message = CreateMessage();

        var result = await sender.SendAsync(
            message,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var reader = queue
            .ReadAllAsync(CancellationToken.None)
            .GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(message, reader.Current);
    }

    [Fact]
    public async Task SendFailsFastWhenQueueIsFull()
    {
        var options = new HelloSmtpOptions { QueueCapacity = 1 };
        var queue = new SmtpHelloAccountMessageQueue(options);
        var sender = new QueuedSmtpHelloAccountMessageProvider(
            options,
            queue);
        var first = await sender.SendAsync(
            CreateMessage(),
            CancellationToken.None);

        var second = await sender.SendAsync(
            CreateMessage(),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Contains(
            second.Errors,
            error =>
                error.Code == HelloDeliveryErrorCodes.QueueFull);
    }

    [Fact]
    public async Task SendRejectsExpiredMessage()
    {
        var options = new HelloSmtpOptions { QueueCapacity = 1 };
        var queue = new SmtpHelloAccountMessageQueue(options);
        var sender = new QueuedSmtpHelloAccountMessageProvider(
            options,
            queue);

        var result = await sender.SendAsync(
            CreateMessage() with
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code == HelloDeliveryErrorCodes.Expired);
    }

    private static HelloAccountMessage CreateMessage()
        => new(
            Guid.NewGuid(),
            HelloAccountMessageKind.PasswordReset,
            HelloDeliveryChannel.Email,
            "alice@example.test",
            new Uri(
                "https://accounts.example.test/hello/reset-password"),
            DateTimeOffset.UtcNow.AddHours(1));
}
