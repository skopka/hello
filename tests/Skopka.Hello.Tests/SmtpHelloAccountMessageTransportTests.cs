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

    private static SmtpHelloAccountMessageTransport CreateTransport()
        => new(
            new HelloSmtpOptions
            {
                Host = "smtp.example.test",
                FromAddress = "accounts@example.test",
            });
}
