using System.Threading.Channels;

namespace Skopka.Hello;

internal sealed record HelloAnonymousAccountMessageRequest(
    Guid MessageId,
    HelloAccountMessageKind Kind,
    string NormalizedTarget);

internal sealed class HelloAnonymousAccountMessageQueue<TProfile>
{
    private readonly Channel<HelloAnonymousAccountMessageRequest> channel;

    public HelloAnonymousAccountMessageQueue(
        HelloDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        channel = Channel.CreateBounded<
            HelloAnonymousAccountMessageRequest>(
                new BoundedChannelOptions(
                    options.AnonymousRequestQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });
    }

    public bool TryWrite(
        HelloAnonymousAccountMessageRequest request)
        => channel.Writer.TryWrite(request);

    public IAsyncEnumerable<HelloAnonymousAccountMessageRequest>
        ReadAllAsync(CancellationToken cancellationToken)
        => channel.Reader.ReadAllAsync(cancellationToken);
}
