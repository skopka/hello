using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Server;

public sealed class HelloProfileUiFactory
    : IHelloUiProfileFactory<HelloProfile>
{
    public OperationResult<HelloProfile> Create(
        HelloUiRegistrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var displayName = profile.DisplayName.Trim();
        if (displayName.Length == 0)
        {
            return OperationResultFactory.Fail<HelloProfile>(
                new Error(
                    IdentityErrorCodes.Validation,
                    "Validation failed.",
                    ErrorType.Validation,
                    new ValidationDetails(
                        new Dictionary<string, string[]>
                        {
                            [nameof(profile.DisplayName)] =
                            [
                                "Display name is required.",
                            ],
                        })));
        }

        return OperationResultFactory.Success(
            new HelloProfile(
                displayName,
                string.IsNullOrWhiteSpace(profile.Locale)
                    ? null
                    : profile.Locale.Trim()));
    }

    public string GetDisplayName(HelloProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.DisplayName;
    }
}
