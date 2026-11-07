using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiRegistrationProfile(
    string DisplayName,
    string? Locale)
{
    public HelloRegistrationConsent? RegistrationConsent { get; init; }
    /// <summary>Validated host-defined registration field values.</summary>
    public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();
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
    int? MaximumLength = null)
{
    /// <summary>
    /// Optional host-owned text rendered under the input and announced as
    /// its accessible description. It is not localized by Hello.
    /// </summary>
    public string? Hint { get; init; }

    /// <summary>Whether the built-in account editor must render the field without editing.</summary>
    public bool ReadOnly { get; init; }
}

public interface IHelloUiProfileEditor<TProfile>
{
    IReadOnlyList<HelloUiProfileField> GetFields(TProfile profile);

    OperationResult<TProfile> Update(
        TProfile current,
        IReadOnlyDictionary<string, string?> values);
}

/// <summary>Supplies and validates host-owned fields for phone registration.</summary>
public interface IHelloUiRegistrationFields
{
    IReadOnlyList<HelloUiProfileField> GetFields();
    OperationResult<IReadOnlyDictionary<string, string?>> Validate(IReadOnlyDictionary<string, string?> values);
}
