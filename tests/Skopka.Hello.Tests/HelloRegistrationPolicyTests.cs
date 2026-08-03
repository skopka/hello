using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Registration;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello.Tests;

public sealed class HelloRegistrationPolicyTests
{
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

    private static SkopkaHelloOptions CreateDisabledOptions()
    {
        var options = new SkopkaHelloOptions
        {
            SelfRegistrationEnabled = false,
        };
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

    private sealed record TestProfile(string DisplayName);
}
