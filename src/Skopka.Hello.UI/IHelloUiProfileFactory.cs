using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiRegistrationProfile(
    string DisplayName,
    string? Locale)
{
    public HelloRegistrationConsent? RegistrationConsent { get; init; }
}

public interface IHelloUiProfileFactory<TProfile>
{
    OperationResult<TProfile> Create(
        HelloUiRegistrationProfile profile);

    string GetDisplayName(TProfile profile);
}

public sealed record HelloUiProfileField(
    string Name,
    string Label,
    string? Value,
    string InputType = "text",
    string? AutoComplete = null,
    bool Required = false,
    int? MaximumLength = null);

public interface IHelloUiProfileEditor<TProfile>
{
    IReadOnlyList<HelloUiProfileField> GetFields(TProfile profile);

    OperationResult<TProfile> Update(
        TProfile current,
        IReadOnlyDictionary<string, string?> values);
}
