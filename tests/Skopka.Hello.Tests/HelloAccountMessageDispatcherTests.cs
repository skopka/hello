using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Tests;

public sealed class HelloAccountMessageDispatcherTests
{
    [Fact]
    public async Task RoutesEachChannelToConfiguredProvider()
    {
        var email = new RecordingProvider(
            "primary-email",
            HelloDeliveryChannel.Email);
        var sms = new RecordingProvider(
            "primary-sms",
            HelloDeliveryChannel.Sms);
        var sender = new HelloAccountMessageDispatcher(
            new HelloDeliveryOptions
            {
                EmailProviderId = "PRIMARY-EMAIL",
                SmsProviderId = "primary-sms",
            },
            [email, sms]);
        var emailMessage = CreateActionMessage(
            HelloDeliveryChannel.Email,
            HelloAccountMessageKind.EmailConfirmation);
        var smsMessage = CreateActionMessage(
            HelloDeliveryChannel.Sms,
            HelloAccountMessageKind.PhoneConfirmation);

        var emailResult = await sender.SendAsync(
            emailMessage,
            CancellationToken.None);
        var smsResult = await sender.SendAsync(
            smsMessage,
            CancellationToken.None);

        Assert.True(emailResult.IsSuccess);
        Assert.True(smsResult.IsSuccess);
        Assert.Same(emailMessage, Assert.Single(email.Messages));
        Assert.Same(smsMessage, Assert.Single(sms.Messages));
    }

    [Fact]
    public void MissingConfiguredProviderFailsStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSkopkaHelloDelivery(options =>
            options.EmailProviderId = "missing");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetServices<IHostedService>().ToArray());

        Assert.Contains("not registered", exception.Message);
    }

    [Fact]
    public void DuplicateProviderIdFailsValidation()
    {
        var providers = new IHelloAccountMessageProvider[]
        {
            new RecordingProvider(
                "duplicate",
                HelloDeliveryChannel.Email),
            new RecordingProvider(
                "DUPLICATE",
                HelloDeliveryChannel.Email),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new HelloAccountMessageDispatcher(
                new HelloDeliveryOptions(),
                providers));

        Assert.Contains("more than once", exception.Message);
    }

    [Fact]
    public void ProviderChannelMismatchFailsValidation()
    {
        var options = new HelloDeliveryOptions
        {
            SmsProviderId = "email-only",
        };
        var provider = new RecordingProvider(
            "email-only",
            HelloDeliveryChannel.Email);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new HelloAccountMessageDispatcher(
                options,
                [provider]));

        Assert.Contains("cannot be configured", exception.Message);
    }

    [Fact]
    public void UnsupportedVerificationChannelFailsValidation()
    {
        var options = new HelloDeliveryOptions
        {
            VerificationChannel = (HelloDeliveryChannel)99,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new HelloAccountMessageDispatcher(options, []));

        Assert.Contains(
            "verification delivery channel",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnconfiguredChannelReturnsStructuredFailure()
    {
        var sender = new HelloAccountMessageDispatcher(
            new HelloDeliveryOptions(),
            []);

        var result = await sender.SendAsync(
            CreateActionMessage(
                HelloDeliveryChannel.Email,
                HelloAccountMessageKind.EmailConfirmation),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(HelloDeliveryErrorCodes.NotConfigured, error.Code);
    }

    [Fact]
    public void AvailabilityReportsUnconfiguredChannel()
    {
        var sender = new HelloAccountMessageDispatcher(
            new HelloDeliveryOptions(),
            []);

        var result = sender.CheckAvailability(
            HelloDeliveryChannel.Sms);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            HelloDeliveryErrorCodes.NotConfigured,
            Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ProviderExceptionReturnsStructuredFailure()
    {
        var sender = new HelloAccountMessageDispatcher(
            new HelloDeliveryOptions
            {
                SmsProviderId = "throwing-sms",
            },
            [new ThrowingProvider()]);

        var result = await sender.SendAsync(
            CreateActionMessage(
                HelloDeliveryChannel.Sms,
                HelloAccountMessageKind.PhoneConfirmation),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            HelloDeliveryErrorCodes.Failed,
            Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedToProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new HelloAccountMessageDispatcher(
            new HelloDeliveryOptions
            {
                SmsProviderId = "canceling-sms",
            },
            [new CancelingProvider(cancellation)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(
                CreateActionMessage(
                    HelloDeliveryChannel.Sms,
                    HelloAccountMessageKind.PhoneConfirmation),
                cancellation.Token));
    }

    [Fact]
    public async Task PublicSmsRegistrationParticipatesInStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSkopkaHelloDelivery(options =>
            options.SmsProviderId = "custom-sms");
        services.AddSkopkaHelloSmsProvider<CustomSmsProvider>();
        await using var provider = services.BuildServiceProvider();

        _ = provider.GetServices<IHostedService>().ToArray();
        var result = await provider
            .GetRequiredService<IHelloAccountMessageSender>()
            .SendAsync(
                CreateActionMessage(
                    HelloDeliveryChannel.Sms,
                    HelloAccountMessageKind.PhoneConfirmation),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(
            provider.GetRequiredService<CustomSmsProvider>().Messages);
    }

    private static HelloAccountMessage CreateActionMessage(
        HelloDeliveryChannel channel,
        HelloAccountMessageKind kind)
        => new(
            Guid.NewGuid(),
            kind,
            channel,
            channel == HelloDeliveryChannel.Email
                ? "alice@example.test"
                : "+12025550123",
            new Uri("https://accounts.example.test/action?token=secret"),
            DateTimeOffset.UtcNow.AddMinutes(10));

    private class RecordingProvider
        : IHelloAccountMessageProvider
    {
        public RecordingProvider(
            string providerId,
            HelloDeliveryChannel channel)
        {
            ProviderId = providerId;
            Channel = channel;
        }

        public string ProviderId { get; }

        public HelloDeliveryChannel Channel { get; }

        public List<HelloAccountMessage> Messages { get; } = [];

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.FromResult(OperationResultFactory.Success());
        }
    }

    private sealed class CustomSmsProvider
        : RecordingProvider
    {
        public CustomSmsProvider()
            : base(
                "custom-sms",
                HelloDeliveryChannel.Sms)
        {
        }
    }

    private sealed class ThrowingProvider
        : IHelloAccountMessageProvider
    {
        public string ProviderId => "throwing-sms";

        public HelloDeliveryChannel Channel => HelloDeliveryChannel.Sms;

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Provider secret details");
    }

    private sealed class CancelingProvider(
        CancellationTokenSource cancellation)
        : IHelloAccountMessageProvider
    {
        public string ProviderId => "canceling-sms";

        public HelloDeliveryChannel Channel => HelloDeliveryChannel.Sms;

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled<OperationResult>(
                cancellationToken);
        }
    }
}
