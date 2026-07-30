using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;

namespace Skopka.Hello.Sample;

public sealed class SampleProfileUiFactory
    : IHelloUiProfileFactory<SampleProfile>
{
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
}
