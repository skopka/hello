namespace Skopka.Hello.Tests;

public sealed class QueuedHelloAccountMessageSenderTests
{
    [Fact]
    public async Task SendQueuesMessageWithoutWaitingForTransport()
    {
        var queue = new HelloAccountMessageQueue(
            new HelloSmtpOptions { QueueCapacity = 1 });
        var sender = new QueuedHelloAccountMessageSender(queue);
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
        var queue = new HelloAccountMessageQueue(
            new HelloSmtpOptions { QueueCapacity = 1 });
        var sender = new QueuedHelloAccountMessageSender(queue);
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

    private static HelloAccountMessage CreateMessage()
        => new(
            HelloAccountMessageKind.PasswordReset,
            "alice@example.test",
            new Uri(
                "https://accounts.example.test/hello/reset-password"),
            DateTimeOffset.UtcNow.AddHours(1));
}
