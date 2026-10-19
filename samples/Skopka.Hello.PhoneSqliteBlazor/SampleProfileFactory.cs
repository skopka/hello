using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;

namespace Skopka.Hello.PhoneSqliteBlazor;

public sealed class SampleProfileFactory : IHelloUiProfileFactory<SampleProfile>
{
    public OperationResult<SampleProfile> Create(
        HelloUiRegistrationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.DisplayName)
            || profile.DisplayName.Length > 200)
        {
            return OperationResultFactory.Fail<SampleProfile>(new Error(
                "profile.display_name_invalid",
                "A display name is required (up to 200 characters).",
                ErrorType.Validation));
        }

        return OperationResultFactory.Success(new SampleProfile(
            profile.DisplayName.Trim(), profile.Locale?.Trim()));
    }

    public string GetDisplayName(SampleProfile profile)
        => profile.DisplayName;

    public IReadOnlyList<HelloUiProfileField> GetFields(
        SampleProfile profile)
        => [new HelloUiProfileField(
            "displayName", "Display name", profile.DisplayName)];
}
