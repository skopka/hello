using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skopka.Hello.Tests;

public sealed class HelloSmtpOptionsTests
{
    [Fact]
    public void ValidateAcceptsAnonymousSmtpConfiguration()
    {
        var options = CreateValidOptions();

        options.Validate();
    }

    [Fact]
    public void ValidateAcceptsAuthenticatedSmtpConfiguration()
    {
        var options = CreateValidOptions();
        options.UserName = "mailer";
        options.Password = "secret";

        options.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void ValidateRejectsInvalidPort(int port)
    {
        var options = CreateValidOptions();
        options.Port = port;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateRejectsPartialCredentials()
    {
        var options = CreateValidOptions();
        options.UserName = "mailer";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void ValidateRejectsInvalidQueueCapacity(int capacity)
    {
        var options = CreateValidOptions();
        options.QueueCapacity = capacity;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateIgnoresQueueCapacityForDirectTransport()
    {
        var options = CreateValidOptions();
        options.UseBackgroundQueue = false;
        options.QueueCapacity = 0;

        options.Validate();
    }

    [Fact]
    public void ValidateRejectsInvalidFromAddress()
    {
        var options = CreateValidOptions();
        options.FromAddress = "not-an-address";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateRejectsInvalidProviderId()
    {
        var options = CreateValidOptions();
        options.ProviderId = "not a provider";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void RegistrationUsesQueuedEmailProviderAndBackgroundWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSkopkaHelloDelivery(options =>
            options.EmailProviderId = "smtp");
        services.AddSkopkaHelloSmtpProvider(options =>
        {
            options.Host = "smtp.example.test";
            options.FromAddress = "accounts@example.test";
        });
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        Assert.IsType<HelloAccountMessageDispatcher>(
            provider.GetRequiredService<
                IHelloAccountMessageSender>());
        var smtpProvider = Assert.Single(
            provider.GetServices<IHelloAccountMessageProvider>());
        Assert.IsType<QueuedSmtpHelloAccountMessageProvider>(
            smtpProvider);
        Assert.Equal("smtp", smtpProvider.ProviderId);
        Assert.Equal(
            HelloDeliveryChannel.Email,
            smtpProvider.Channel);
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service =>
                service is SmtpHelloAccountMessageWorker);
    }

    [Fact]
    public void RegistrationCanUseDirectEmailProviderWithoutWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSkopkaHelloDelivery(options =>
            options.EmailProviderId = "smtp");
        services.AddSkopkaHelloSmtpProvider(options =>
        {
            options.Host = "smtp.example.test";
            options.FromAddress = "accounts@example.test";
            options.UseBackgroundQueue = false;
        });
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        var smtpProvider = Assert.Single(
            provider.GetServices<IHelloAccountMessageProvider>());
        Assert.IsType<DirectSmtpHelloAccountMessageProvider>(
            smtpProvider);
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service is SmtpHelloAccountMessageWorker);
    }

    private static HelloSmtpOptions CreateValidOptions()
        => new()
        {
            Host = "smtp.example.test",
            FromAddress = "accounts@example.test",
        };
}
