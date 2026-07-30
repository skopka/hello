using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiRegistrationProfile(
    string DisplayName,
    string? Locale);

public interface IHelloUiProfileFactory<TProfile>
{
    OperationResult<TProfile> Create(
        HelloUiRegistrationProfile profile);

    string GetDisplayName(TProfile profile);
}
