using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;

namespace Skopka.Hello.Sample;

public sealed class SampleProfileUiFactory
    : IHelloUiProfileFactory<SampleProfile>,
        IHelloUiProfileEditor<SampleProfile>
{
    private const string DisplayNameField = "displayName";
    private const string LocaleField = "locale";

    public OperationResult<SampleProfile> Create(
        HelloUiRegistrationProfile profile)
        => OperationResultFactory.Success(
            new SampleProfile(
                profile.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(profile.Locale)
                    ? null
                    : profile.Locale.Trim()));

    public string GetDisplayName(SampleProfile profile)
        => profile.DisplayName;

    public IReadOnlyList<HelloUiProfileField> GetFields(
        SampleProfile profile)
        =>
        [
            new HelloUiProfileField(
                DisplayNameField,
                "Display name",
                profile.DisplayName,
                AutoComplete: "name",
                Required: true,
                MaximumLength: 200),
            new HelloUiProfileField(
                LocaleField,
                "Locale",
                profile.Locale,
                AutoComplete: "language",
                MaximumLength: 32),
        ];

    public OperationResult<SampleProfile> Update(
        SampleProfile current,
        IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(values);

        values.TryGetValue(DisplayNameField, out var displayNameValue);
        values.TryGetValue(LocaleField, out var localeValue);
        var displayName = displayNameValue?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return OperationResultFactory.Fail<SampleProfile>(
                new Error(
                    "sample.profile.display_name_required",
                    "Display name is required.",
                    ErrorType.Validation));
        }

        return OperationResultFactory.Success(
            new SampleProfile(
                displayName,
                string.IsNullOrWhiteSpace(localeValue)
                    ? null
                    : localeValue.Trim()));
    }
}
