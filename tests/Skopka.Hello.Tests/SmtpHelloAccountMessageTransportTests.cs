namespace Skopka.Hello.Tests;

public sealed class SmtpHelloAccountMessageTransportTests
{
    [Theory]
    [InlineData(
        HelloAccountMessageKind.PasswordChangeVerification,
        "Confirm your password change",
        "change your password")]
    [InlineData(
        HelloAccountMessageKind.ExternalLoginLinkVerification,
        "Confirm external sign-in linking",
        "link an external sign-in provider")]
    [InlineData(
        HelloAccountMessageKind.ExternalLoginUnlinkVerification,
        "Confirm external sign-in removal",
        "remove an external sign-in provider")]
    [InlineData(
        HelloAccountMessageKind.AccountSecurityVerification,
        "Confirm account security action",
        "authorize the requested account security action")]
    [InlineData(
        HelloAccountMessageKind.AdminActionVerification,
        "Confirm administrative action",
        "authorize the requested administrative action")]
    public void VerificationKindsRenderPurposeSpecificMultipartEmail(
        HelloAccountMessageKind kind,
        string expectedSubject,
        string expectedText)
    {
        var transport = CreateTransport();
        var message = new HelloAccountMessage(
            Guid.NewGuid(),
            kind,
            HelloDeliveryChannel.Email,
            "alice@example.test",
            null,
            DateTimeOffset.UtcNow.AddMinutes(5),
            "123456");

        using var mail = transport.CreateMessage(message);

        Assert.Equal(expectedSubject, mail.Subject);
        Assert.Contains(expectedText, mail.Body);
        Assert.Contains("123456", mail.Body);
        Assert.Single(mail.AlternateViews);
    }

    [Theory]
    [InlineData(
        HelloAccountMessageKind.PasswordReset,
        "Reset your password")]
    [InlineData(
        HelloAccountMessageKind.EmailConfirmation,
        "Confirm your email address")]
    [InlineData(
        HelloAccountMessageKind.PhoneConfirmation,
        "Confirm your phone number")]
    public void ActionKindsRenderPlainTextAndHtml(
        HelloAccountMessageKind kind,
        string expectedSubject)
    {
        var transport = CreateTransport();
        var actionUrl = new Uri(
            "https://accounts.example.test/action?token=secret");
        var message = new HelloAccountMessage(
            Guid.NewGuid(),
            kind,
            HelloDeliveryChannel.Email,
            "alice@example.test",
            actionUrl,
            DateTimeOffset.UtcNow.AddMinutes(5));

        using var mail = transport.CreateMessage(message);

        Assert.Equal(expectedSubject, mail.Subject);
        Assert.Contains(actionUrl.AbsoluteUri, mail.Body);
        Assert.Single(mail.AlternateViews);
    }

    [Fact]
    public void PackagedRussianDictionaryRendersRussianEmail()
    {
        var transport = CreateTransport(options =>
            options.Localization.DefaultCulture = "ru");
        var message = new HelloAccountMessage(
            Guid.NewGuid(),
            HelloAccountMessageKind.AdminActionVerification,
            HelloDeliveryChannel.Email,
            "alice@example.test",
            null,
            DateTimeOffset.UtcNow.AddMinutes(5),
            "123456");

        using var mail = transport.CreateMessage(message);

        Assert.Equal(
            "Подтверждение административного действия",
            mail.Subject);
        Assert.Contains("Используйте этот код", mail.Body);
        Assert.Contains("Код действует до", mail.Body);
    }

    [Fact]
    public void HostDictionaryOverridesStableKeys()
    {
        var directory = Directory.CreateTempSubdirectory(
            "skopka-hello-email-");
        try
        {
            var dictionaryPath = Path.Combine(
                directory.FullName,
                "ru.json");
            File.WriteAllText(
                dictionaryPath,
                """
                {
                  "culture": "ru",
                  "texts": {
                    "Email.AccountSecurityVerification.AccountDelete.Subject": "Важное действие в школе",
                    "Email.AccountSecurityVerification.AccountDelete.Introduction": "Удаление аккаунта необратимо: вместе с ним исчезнут зачисления и статистика."
                  }
                }
                """);
            var transport = CreateTransport(options =>
            {
                options.Localization.DefaultCulture = "ru";
                options.Localization.AddDictionaryFile(
                    "ru",
                    dictionaryPath);
            });
            var message = new HelloAccountMessage(
                Guid.NewGuid(),
                HelloAccountMessageKind.AccountSecurityVerification,
                HelloDeliveryChannel.Email,
                "alice@example.test",
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                "123456",
                HelloAccountEmailTemplateVariants.AccountDelete);

            using var mail = transport.CreateMessage(message);

            Assert.Equal("Важное действие в школе", mail.Subject);
            Assert.Contains("удаление аккаунта", mail.Body,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SmtpHelloAccountMessageTransport CreateTransport(
        Action<HelloSmtpOptions>? configure = null)
    {
        var options = new HelloSmtpOptions
        {
            Host = "smtp.example.test",
            FromAddress = "accounts@example.test",
        };
        configure?.Invoke(options);
        options.Validate();
        var catalog = new HelloAccountEmailTextCatalog(options);
        var renderer = new HelloAccountEmailTemplateRenderer(catalog);
        return new SmtpHelloAccountMessageTransport(
            options,
            renderer);
    }
}
