using System.Threading.Channels;

namespace Skopka.Hello;

internal sealed class SmtpHelloAccountMessageQueue(
    HelloSmtpOptions options)
{
    private readonly Channel<HelloAccountMessage> channel =
        Channel.CreateBounded<HelloAccountMessage>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

    public bool TryWrite(HelloAccountMessage message)
        => channel.Writer.TryWrite(message);

    public IAsyncEnumerable<HelloAccountMessage> ReadAllAsync(
        CancellationToken cancellationToken)
        => channel.Reader.ReadAllAsync(cancellationToken);
}
