using System.Threading.Channels;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public sealed record HelloAnonymousAccountMessageRequest(
    Guid MessageId,
    HelloAccountMessageKind Kind,
    string NormalizedTarget);

public sealed record HelloAnonymousAccountMessageLease(
    Guid LeaseId,
    HelloAnonymousAccountMessageRequest Request);

public interface IHelloAnonymousAccountMessageInbox
{
    Task<OperationResult> EnqueueAsync(
        HelloAnonymousAccountMessageRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<HelloAnonymousAccountMessageLease> ReadAllAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> CompleteAsync(
        HelloAnonymousAccountMessageLease lease,
        CancellationToken cancellationToken);

    Task<OperationResult> FailAsync(
        HelloAnonymousAccountMessageLease lease,
        string errorCode,
        CancellationToken cancellationToken);
}

internal sealed class InMemoryHelloAnonymousAccountMessageInbox
    : IHelloAnonymousAccountMessageInbox
{
    private readonly Channel<HelloAnonymousAccountMessageRequest> channel;

    public InMemoryHelloAnonymousAccountMessageInbox(
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

    public Task<OperationResult> EnqueueAsync(
        HelloAnonymousAccountMessageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var result = channel.Writer.TryWrite(request)
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.QueueFull,
                    "The anonymous account-message queue is full.",
                    ErrorType.Failure));
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<HelloAnonymousAccountMessageLease>
        ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(
            cancellationToken))
        {
            yield return new HelloAnonymousAccountMessageLease(
                request.MessageId,
                request);
        }
    }

    public Task<OperationResult> CompleteAsync(
        HelloAnonymousAccountMessageLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResultFactory.Success());
    }

    public Task<OperationResult> FailAsync(
        HelloAnonymousAccountMessageLease lease,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResultFactory.Success());
    }
}
