using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public interface IHelloAuditOutbox
{
    Task<OperationResult> AddAsync(
        HelloAuditOutboxRecord record,
        CancellationToken cancellationToken);
}
