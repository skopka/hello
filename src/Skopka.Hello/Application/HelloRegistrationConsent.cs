using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public sealed record HelloRegistrationConsent(
    bool TermsOfServiceAccepted,
    bool PrivacyPolicyAccepted,
    DateTimeOffset? AcceptedAt)
{
    public static HelloRegistrationConsent None { get; } =
        new(false, false, null);
}

public sealed record HelloRegistrationConsentRequirement(
    bool TermsOfServiceRequired,
    bool PrivacyPolicyRequired);

public sealed class HelloRegistrationConsentOptions
{
    public bool TermsOfServiceRequired { get; set; }

    public bool PrivacyPolicyRequired { get; set; }
}

public interface IHelloRegistrationConsentPolicy
{
    OperationResult<HelloRegistrationConsent> Validate(
        HelloRegistrationConsent? consent);
}

public interface IHelloRegistrationConsentProfileEnricher<TProfile>
{
    OperationResult<TProfile> Enrich(
        TProfile profile,
        HelloRegistrationConsent consent);
}

internal sealed class HelloRegistrationConsentPolicy(
    SkopkaHelloOptions options,
    IEnumerable<HelloRegistrationConsentRequirement> requirements)
    : IHelloRegistrationConsentPolicy
{
    private readonly HelloRegistrationConsentRequirement[] requirements =
        requirements.ToArray();

    public OperationResult<HelloRegistrationConsent> Validate(
        HelloRegistrationConsent? consent)
    {
        var termsRequired =
            options.RegistrationConsent.TermsOfServiceRequired
            || requirements.Any(requirement =>
                requirement.TermsOfServiceRequired);
        var privacyRequired =
            options.RegistrationConsent.PrivacyPolicyRequired
            || requirements.Any(requirement =>
                requirement.PrivacyPolicyRequired);
        var hasAcceptanceMoment = consent?.AcceptedAt is not null;
        var termsMissing = termsRequired
            && !(consent?.TermsOfServiceAccepted is true
                && hasAcceptanceMoment);
        var privacyMissing = privacyRequired
            && !(consent?.PrivacyPolicyAccepted is true
                && hasAcceptanceMoment);

        if (termsMissing || privacyMissing)
        {
            return OperationResultFactory.Fail<
                HelloRegistrationConsent>(
                HelloRegistrationErrors.ConsentRequired(
                    termsMissing,
                    privacyMissing));
        }

        return OperationResultFactory.Success(
            termsRequired || privacyRequired
                ? new HelloRegistrationConsent(
                    termsRequired,
                    privacyRequired,
                    consent!.AcceptedAt)
                : HelloRegistrationConsent.None);
    }
}
