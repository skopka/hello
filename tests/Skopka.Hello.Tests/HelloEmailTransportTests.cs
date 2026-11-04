using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Tests;

public sealed class HelloEmailTransportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CustomScopedTransportReceivesRenderedHostDictionaryInEitherRegistrationOrder(
        bool registerTransportFirst)
    {
        var directory = Directory.CreateTempSubdirectory("hello-email-transport-");
        try
        {
            var dictionaryPath = Path.Combine(directory.FullName, "ru.json");
            File.WriteAllText(dictionaryPath, """
                {
                  "culture": "ru",
                  "texts": {
                    "Email.PasswordReset.Subject": "Школьный пароль",
                    "Email.PasswordReset.Introduction": "Восстановление доступа",
                    "Email.PasswordReset.Action": "Открыть ссылку"
                  }
                }
                """);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TransportCapture>();
            services.AddSkopkaHelloDelivery(options => options.EmailProviderId = "smtp");
            if (registerTransportFirst)
                services.AddSkopkaHelloEmailTransport<CaptureTransport>();
            services.AddSkopkaHelloSmtpProvider(options =>
            {
                options.Host = "smtp.example.test";
                options.FromAddress = "sender@example.test";
                options.FromName = "School";
                options.UseBackgroundQueue = false;
                options.Localization.DefaultCulture = "ru";
                options.Localization.AddDictionaryFile("ru", dictionaryPath);
            });
            if (!registerTransportFirst)
                services.AddSkopkaHelloEmailTransport<CaptureTransport>();

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
            var sender = provider.GetRequiredService<IHelloAccountMessageSender>();
            var message = CreateMessage();
            Assert.True((await sender.SendAsync(message, CancellationToken.None)).IsSuccess);
            Assert.True((await sender.SendAsync(message with { MessageId = Guid.NewGuid() }, CancellationToken.None)).IsSuccess);

            var capture = provider.GetRequiredService<TransportCapture>();
            Assert.Equal(2, capture.Emails.Count);
            Assert.Equal(2, capture.Instances.Distinct().Count());
            Assert.Equal(2, capture.Disposed);
            var email = capture.Emails[0];
            Assert.Equal("sender@example.test", email.FromAddress);
            Assert.Equal("School", email.FromName);
            Assert.Equal(message.RecipientAddress, email.RecipientAddress);
            Assert.Equal(message.Kind, email.Kind);
            Assert.Equal("Школьный пароль", email.Subject);
            Assert.Contains("Восстановление доступа", email.TextBody);
            Assert.Contains("Открыть ссылку", email.HtmlBody);
            Assert.Contains(message.ActionUrl!.AbsoluteUri, email.TextBody);
            Assert.Contains("token=secret&amp;next=home", email.HtmlBody);
            Assert.DoesNotContain("alice@example.test", email.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("token=secret", email.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(HelloEmailTransportErrorCodes.Retryable, HelloEmailTransportErrorCodes.Retryable)]
    [InlineData(HelloEmailTransportErrorCodes.Permanent, HelloEmailTransportErrorCodes.Permanent)]
    [InlineData("provider-secret", HelloEmailTransportErrorCodes.Retryable)]
    public async Task ProviderFailuresAreClassifiedAndSanitized(string providerCode, string expectedCode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var capture = new TransportCapture { FailureCode = providerCode };
        services.AddSingleton(capture);
        services.AddSkopkaHelloEmailTransport<CaptureTransport>();
        services.AddSkopkaHelloSmtpProvider(options =>
        {
            options.Host = "smtp.example.test";
            options.FromAddress = "sender@example.test";
            options.UseBackgroundQueue = false;
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var transport = provider.GetRequiredService<SmtpHelloAccountMessageTransport>();

        var result = await transport.SendAsync(CreateMessage(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(expectedCode, error.Code);
        Assert.DoesNotContain("provider-secret", error.ToString(), StringComparison.Ordinal);
    }

    private static HelloAccountMessage CreateMessage() => new(
        Guid.NewGuid(),
        HelloAccountMessageKind.PasswordReset,
        HelloDeliveryChannel.Email,
        "alice@example.test",
        new Uri("https://accounts.example.test/action?token=secret&next=home"),
        DateTimeOffset.UtcNow.AddMinutes(5));

    public sealed class TransportCapture
    {
        public List<HelloEmail> Emails { get; } = [];
        public List<Guid> Instances { get; } = [];
        public int Disposed { get; set; }
        public string? FailureCode { get; set; }
    }

    public sealed class CaptureTransport(TransportCapture capture) : IHelloEmailTransport, IDisposable
    {
        private readonly Guid instance = Guid.NewGuid();

        public Task<OperationResult> SendAsync(HelloEmail email, CancellationToken cancellationToken)
        {
            capture.Emails.Add(email);
            capture.Instances.Add(instance);
            return Task.FromResult(capture.FailureCode is null
                ? OperationResultFactory.Success()
                : OperationResultFactory.Fail(new Error(capture.FailureCode, "provider-secret", ErrorType.Failure)));
        }

        public void Dispose() => capture.Disposed++;
    }
}
