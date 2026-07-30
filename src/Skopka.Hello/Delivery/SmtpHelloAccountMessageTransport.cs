using System.Net;
using System.Net.Mail;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class SmtpHelloAccountMessageTransport(
    HelloSmtpOptions options)
{
    public async Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var mail = CreateMessage(message);
            using var client = new SmtpClient(
                options.Host,
                options.Port)
            {
                EnableSsl = options.EnableSsl,
                UseDefaultCredentials = false,
            };
            if (!string.IsNullOrWhiteSpace(options.UserName))
            {
                client.Credentials = new NetworkCredential(
                    options.UserName,
                    options.Password);
            }

            await client.SendMailAsync(
                mail,
                cancellationToken);
            return OperationResultFactory.Success();
        }
        catch (SmtpException)
        {
            return DeliveryFailed();
        }
        catch (InvalidOperationException)
        {
            return DeliveryFailed();
        }
        catch (FormatException)
        {
            return DeliveryFailed();
        }
    }

    private MailMessage CreateMessage(
        HelloAccountMessage message)
    {
        var (subject, introduction, linkText) = message.Kind switch
        {
            HelloAccountMessageKind.PasswordReset => (
                "Reset your password",
                "A password reset was requested for your account.",
                "Reset password"),
            HelloAccountMessageKind.EmailConfirmation => (
                "Confirm your email address",
                "Confirm the email address for your account.",
                "Confirm email"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(message),
                message.Kind,
                "The account message kind is unsupported."),
        };
        var encodedUrl = WebUtility.HtmlEncode(
            message.ActionUrl.AbsoluteUri);
        var encodedIntroduction = WebUtility.HtmlEncode(
            introduction);
        var encodedLinkText = WebUtility.HtmlEncode(linkText);
        var expires = WebUtility.HtmlEncode(
            message.ExpiresAt.ToUniversalTime().ToString("u"));

        return new MailMessage
        {
            From = new MailAddress(
                options.FromAddress,
                options.FromName),
            To = { new MailAddress(message.RecipientAddress) },
            Subject = subject,
            IsBodyHtml = true,
            Body = $"""
                <p>{encodedIntroduction}</p>
                <p><a href="{encodedUrl}">{encodedLinkText}</a></p>
                <p>This link expires at {expires} UTC.</p>
                <p>If you did not request this action, ignore this message.</p>
                """,
        };
    }

    private static OperationResult DeliveryFailed()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.Failed,
                "The account message could not be delivered.",
                ErrorType.Failure));
}
