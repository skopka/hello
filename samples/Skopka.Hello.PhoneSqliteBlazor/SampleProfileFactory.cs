using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;

namespace Skopka.Hello.PhoneSqliteBlazor;

public sealed class SampleProfileFactory
    : IHelloUiProfileFactory<SampleProfile>,
      IHelloUiProfileEditor<SampleProfile>
{
    private const string DisplayNameField = "displayName";
    private const int MaximumDisplayNameLength = 200;

    public OperationResult<SampleProfile> Create(
        HelloUiRegistrationProfile profile)
    {
        if (!IsValidDisplayName(profile.DisplayName))
        {
            return InvalidDisplayName();
        }

        return OperationResultFactory.Success(new SampleProfile(
            profile.DisplayName.Trim(), profile.Locale?.Trim()));
    }

    public string GetDisplayName(SampleProfile profile)
        => profile.DisplayName;

    public IReadOnlyList<HelloUiProfileField> GetFields(
        SampleProfile profile)
        => [new HelloUiProfileField(
            DisplayNameField, "Display name", profile.DisplayName,
            AutoComplete: "name", Required: true,
            MaximumLength: MaximumDisplayNameLength)
        {
            Hint = "Shown on the private page after sign-in.",
        }];

    public OperationResult<SampleProfile> Update(
        SampleProfile current,
        IReadOnlyDictionary<string, string?> values)
    {
        values.TryGetValue(DisplayNameField, out var displayName);
        if (!IsValidDisplayName(displayName))
        {
            return InvalidDisplayName();
        }

        return OperationResultFactory.Success(
            current with { DisplayName = displayName.Trim() });
    }

    private static bool IsValidDisplayName(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        string? displayName)
        => !string.IsNullOrWhiteSpace(displayName)
            && displayName.Length <= MaximumDisplayNameLength;

    private static OperationResult<SampleProfile> InvalidDisplayName()
        => OperationResultFactory.Fail<SampleProfile>(new Error(
            "profile.display_name_invalid",
            "A display name is required (up to 200 characters).",
            ErrorType.Validation));
}
