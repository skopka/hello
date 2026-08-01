namespace Skopka.Hello.Oidc;

internal sealed class InMemoryHelloOidcFlowStore : IHelloOidcFlowStore
{
    private const int Capacity = 100_000;

    private readonly Lock sync = new();
    private readonly Dictionary<Guid, DateTimeOffset> consumed = [];
    private readonly PriorityQueue<Guid, DateTimeOffset> expirations = new();

    public Task<bool> TryConsumeAsync(
        Guid flowId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        if (flowId == Guid.Empty || expiresAt <= now)
        {
            return Task.FromResult(false);
        }

        lock (sync)
        {
            RemoveExpired(now);
            if (consumed.ContainsKey(flowId)
                || consumed.Count >= Capacity)
            {
                return Task.FromResult(false);
            }

            consumed.Add(flowId, expiresAt);
            expirations.Enqueue(flowId, expiresAt);
            return Task.FromResult(true);
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        while (expirations.TryPeek(out var flowId, out var expiresAt)
            && expiresAt <= now)
        {
            expirations.Dequeue();
            consumed.Remove(flowId);
        }
    }
}
