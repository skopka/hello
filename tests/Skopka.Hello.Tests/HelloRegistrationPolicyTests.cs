using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Registration;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello.Tests;

public sealed class HelloRegistrationPolicyTests
{
    [Fact]
    public void PolicyClearsConsentThatWasNotRequired()
    {
        var options = new SkopkaHelloOptions();
        options.Validate();
        var policy = new HelloRegistrationConsentPolicy(options, []);

        var result = policy.Validate(
            new HelloRegistrationConsent(
                true,
                true,
                new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero)));

        Assert.True(result.IsSuccess);
        Assert.Same(HelloRegistrationConsent.None, result.Value);
    }

    [Fact]
    public async Task DisabledPolicyStopsPasswordRegistrationOperation()
    {
        var options = CreateDisabledOptions();
        var application = new HelloIdentityApplication<TestProfile>(
            registration: new UnexpectedRegistrationService(),
            authentication: null!,
            sessions: null!,
            credentials: null!,
            users: null!,
            stepUp: null!,
            verification: null!,
            anonymousMessageRequester: null!,
            messageSender: CreateMessageSender(),
            deliveryOptions: new HelloDeliveryOptions(),
            options: options);

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TestProfile>(
                "alice",
                "alice@example.test",
                null,
                new TestProfile("Alice"),
                "not-used"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            HelloRegistrationErrors.DisabledCode,
            error.Code);
        Assert.Equal(ErrorType.Forbidden, error.Type);
    }

    [Fact]
    public async Task DisabledPolicyStopsExternalRegistrationOperation()
    {
        var options = CreateDisabledOptions();
        var application =
            new HelloExternalIdentityApplication<TestProfile>(
                null!,
                new UnexpectedRegistrationService(),
                null!,
                null!,
                null!,
                null!,
                CreateMessageSender(),
                new HelloDeliveryOptions(),
                options);

        var result = await application.RegisterAsync(
            new HelloExternalRegistrationCommand<TestProfile>(
                "alice",
                "alice@example.test",
                null,
                new TestProfile("Alice"),
                new Skopka.Identity.ExternalLogins.ExternalLoginKey(
                    "github",
                    "subject"),
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            HelloRegistrationErrors.DisabledCode,
            error.Code);
        Assert.Equal(ErrorType.Forbidden, error.Type);
    }

    [Fact]
    public async Task RequiredConsentStopsPasswordRegistrationOperation()
    {
        var options = CreateConsentRequiredOptions();
        var application = new HelloIdentityApplication<TestProfile>(
            registration: new UnexpectedRegistrationService(),
            authentication: null!,
            sessions: null!,
            credentials: null!,
            users: null!,
            stepUp: null!,
            verification: null!,
            anonymousMessageRequester: null!,
            messageSender: CreateMessageSender(),
            deliveryOptions: new HelloDeliveryOptions(),
            options: options);

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TestProfile>(
                "alice",
                "alice@example.test",
                null,
                new TestProfile("Alice"),
                "not-used"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            HelloRegistrationErrors.ConsentRequiredCode,
            error.Code);
        var details = Assert.IsType<ValidationDetails>(error.Details);
        Assert.Equal(
            ["acceptPrivacyPolicy", "acceptTermsOfService"],
            details.Fields.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RequiredConsentStopsExternalRegistrationOperation()
    {
        var options = CreateConsentRequiredOptions();
        var application =
            new HelloExternalIdentityApplication<TestProfile>(
                null!,
                new UnexpectedRegistrationService(),
                null!,
                null!,
                null!,
                null!,
                CreateMessageSender(),
                new HelloDeliveryOptions(),
                options);

        var result = await application.RegisterAsync(
            new HelloExternalRegistrationCommand<TestProfile>(
                "alice",
                "alice@example.test",
                null,
                new TestProfile("Alice"),
                new Skopka.Identity.ExternalLogins.ExternalLoginKey(
                    "github",
                    "subject"),
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            HelloRegistrationErrors.ConsentRequiredCode,
            Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task AcceptedFlagsWithoutAcceptanceMomentAreRejected()
    {
        var options = CreateConsentRequiredOptions();
        var application = new HelloIdentityApplication<TestProfile>(
            registration: new UnexpectedRegistrationService(),
            authentication: null!,
            sessions: null!,
            credentials: null!,
            users: null!,
            stepUp: null!,
            verification: null!,
            anonymousMessageRequester: null!,
            messageSender: CreateMessageSender(),
            deliveryOptions: new HelloDeliveryOptions(),
            options: options);

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TestProfile>(
                "alice",
                "alice@example.test",
                null,
                new TestProfile("Alice"),
                "not-used")
            {
                RegistrationConsent = new HelloRegistrationConsent(
                    true,
                    true,
                    AcceptedAt: null),
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            HelloRegistrationErrors.ConsentRequiredCode,
            Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task TrustedConsentReachesProfileEnricherBeforeIdentity()
    {
        var options = CreateConsentRequiredOptions();
        var enricher = new RecordingConsentProfileEnricher();
        var application = new HelloIdentityApplication<TestProfile>(
            registration: new UnexpectedRegistrationService(),
            authentication: null!,
            sessions: null!,
            credentials: null!,
            users: null!,
            stepUp: null!,
            verification: null!,
            anonymousMessageRequester: null!,
            messageSender: CreateMessageSender(),
            deliveryOptions: new HelloDeliveryOptions(),
            options: options,
            registrationConsentProfileEnricher: enricher);
        var consent = new HelloRegistrationConsent(
            true,
            true,
            new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TestProfile>(
                "alice",
                "alice@example.test",
                null,
                new TestProfile("Alice"),
                "not-used")
            {
                RegistrationConsent = consent,
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(consent, enricher.Consent);
        Assert.Equal("test.consent_profile", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task PasswordRegistrationRequiresAUsableLoginHandle()
    {
        var options = new SkopkaHelloOptions();
        options.Validate();
        var application = new HelloIdentityApplication<TestProfile>(
            registration: new UnexpectedRegistrationService(),
            authentication: null!,
            sessions: null!,
            credentials: null!,
            users: null!,
            stepUp: null!,
            verification: null!,
            anonymousMessageRequester: null!,
            messageSender: CreateMessageSender(),
            deliveryOptions: new HelloDeliveryOptions(),
            options: options);

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TestProfile>(
                null,
                " ",
                null,
                new TestProfile("Alice"),
                "not-used"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(IdentityErrorCodes.Validation, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
        var details = Assert.IsType<ValidationDetails>(error.Details);
        Assert.Equal(
            ["email", "phone", "userName"],
            details.Fields.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PasswordRegistrationRequiresConfiguredUserNameHandle()
    {
        var options = new SkopkaHelloOptions
        {
            PasswordLoginHandle = Skopka.Identity.Authentication
                .PasswordLoginHandle.UserName,
        };
        options.Validate();
        var application = new HelloIdentityApplication<TestProfile>(
            registration: new UnexpectedRegistrationService(),
            authentication: null!,
            sessions: null!,
            credentials: null!,
            users: null!,
            stepUp: null!,
            verification: null!,
            anonymousMessageRequester: null!,
            messageSender: CreateMessageSender(),
            deliveryOptions: new HelloDeliveryOptions(),
            options: options);

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TestProfile>(
                null,
                "alice@example.test",
                null,
                new TestProfile("Alice"),
                "not-used"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            IdentityErrorCodes.Validation,
            Assert.Single(result.Errors).Code);
    }

    private static SkopkaHelloOptions CreateDisabledOptions()
    {
        var options = new SkopkaHelloOptions
        {
            SelfRegistrationEnabled = false,
        };
        options.Validate();
        return options;
    }

    private static SkopkaHelloOptions CreateConsentRequiredOptions()
    {
        var options = new SkopkaHelloOptions();
        options.RegistrationConsent.TermsOfServiceRequired = true;
        options.RegistrationConsent.PrivacyPolicyRequired = true;
        options.Validate();
        return options;
    }

    private static HelloAccountMessageDispatcher CreateMessageSender()
        => new HelloAccountMessageDispatcher(
            new HelloDeliveryOptions(),
            []);

    private sealed class UnexpectedRegistrationService
        : IIdentityRegistrationService<TestProfile>
    {
        public Task<OperationResult<IdentityUser<TestProfile>>>
            RegisterPasswordAsync(
                RegisterPasswordUserCommand<TestProfile> command,
                CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Password registration must not be called.");

        public Task<OperationResult<IdentityUser<TestProfile>>>
            RegisterExternalAsync(
                RegisterExternalUserCommand<TestProfile> command,
                CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "External registration must not be called.");
    }

    private sealed class RecordingConsentProfileEnricher
        : IHelloRegistrationConsentProfileEnricher<TestProfile>
    {
        public HelloRegistrationConsent? Consent { get; private set; }

        public OperationResult<TestProfile> Enrich(
            TestProfile profile,
            HelloRegistrationConsent consent)
        {
            Consent = consent;
            return OperationResultFactory.Fail<TestProfile>(
                new Error(
                    "test.consent_profile",
                    "Consent profile mapping stopped registration.",
                    ErrorType.Validation));
        }
    }

    private sealed record TestProfile(string DisplayName);
}
