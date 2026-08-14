using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class SmtpHelloAccountMessageTransport(
    HelloSmtpOptions options,
    HelloAccountEmailTemplateRenderer renderer)
{
    public async Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (message.Channel != HelloDeliveryChannel.Email)
        {
            return OperationResultFactory.Fail(
                HelloAccountMessageValidator.ChannelMismatch());
        }

        var validation = HelloAccountMessageValidator.Validate(
            message,
            DateTimeOffset.UtcNow);
        if (validation is not null)
        {
            return OperationResultFactory.Fail(validation);
        }

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

    internal MailMessage CreateMessage(
        HelloAccountMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var content = renderer.Render(message);

        var mail = new MailMessage
        {
            From = new MailAddress(
                options.FromAddress,
                options.FromName),
            To = { new MailAddress(message.RecipientAddress) },
            Subject = content.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = content.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(
                content.HtmlBody,
                Encoding.UTF8,
                MediaTypeNames.Text.Html));
        return mail;
    }

    private static OperationResult DeliveryFailed()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.Failed,
                "The account message could not be delivered.",
                ErrorType.Failure));
}
