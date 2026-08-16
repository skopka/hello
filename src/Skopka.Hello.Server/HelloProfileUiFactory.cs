using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Server;

public sealed class HelloProfileUiFactory
    : IHelloUiProfileFactory<HelloProfile>,
        IHelloUiProfileEditor<HelloProfile>,
        IHelloRegistrationConsentProfileEnricher<HelloProfile>
{
    private const string DisplayNameField = "displayName";
    private const string LocaleField = "locale";

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
                    : profile.Locale.Trim())
            {
                RegistrationConsent = ToProfileConsent(
                    profile.RegistrationConsent),
            });
    }

    public string GetDisplayName(HelloProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.DisplayName;
    }

    public IReadOnlyList<HelloUiProfileField> GetFields(
        HelloProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return
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
    }

    public OperationResult<HelloProfile> Update(
        HelloProfile current,
        IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(values);

        values.TryGetValue(DisplayNameField, out var displayNameValue);
        values.TryGetValue(LocaleField, out var localeValue);
        var displayName = displayNameValue?.Trim();
        var locale = string.IsNullOrWhiteSpace(localeValue)
            ? null
            : localeValue.Trim();
        Dictionary<string, string[]> errors = [];
        if (string.IsNullOrWhiteSpace(displayName))
        {
            errors[DisplayNameField] = ["Display name is required."];
        }
        else if (displayName.Length > 200)
        {
            errors[DisplayNameField] =
                ["Display name cannot exceed 200 characters."];
        }

        if (locale?.Length > 32)
        {
            errors[LocaleField] =
                ["Locale cannot exceed 32 characters."];
        }

        return errors.Count == 0
            ? OperationResultFactory.Success(
                new HelloProfile(displayName!, locale)
                {
                    RegistrationConsent =
                        current.RegistrationConsent,
                })
            : OperationResultFactory.Fail<HelloProfile>(
                new Error(
                    IdentityErrorCodes.Validation,
                    "Validation failed.",
                    ErrorType.Validation,
                    new ValidationDetails(errors)));
    }

    public OperationResult<HelloProfile> Enrich(
        HelloProfile profile,
        HelloRegistrationConsent consent)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(consent);

        return OperationResultFactory.Success(
            profile with
            {
                RegistrationConsent = ToProfileConsent(consent),
            });
    }

    private static HelloProfileRegistrationConsent? ToProfileConsent(
        HelloRegistrationConsent? consent)
        => consent is { AcceptedAt: { } acceptedAt }
            ? new HelloProfileRegistrationConsent(
                consent.TermsOfServiceAccepted,
                consent.PrivacyPolicyAccepted,
                acceptedAt)
            : null;
}
