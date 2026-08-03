using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Admin;

namespace Skopka.Hello.Server;

public sealed class HelloAdminProfileProjector
    : IHelloAdminProfileProjector<HelloProfile>
{
    public Task<OperationResult<IReadOnlyList<HelloAdminProfileField>>>
        ProjectAsync(
            HelloProfile profile,
            HelloAdminProfileProjectionContext context,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<HelloAdminProfileField> fields =
        [
            new("displayName", "Display name", profile.DisplayName),
            new("locale", "Locale", profile.Locale),
        ];
        return Task.FromResult(OperationResultFactory.Success(fields));
    }
}
