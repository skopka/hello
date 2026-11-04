using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class SmtpHelloEmailTransport(HelloSmtpOptions options) : IHelloEmailTransport
{
    public async Task<OperationResult> SendAsync(HelloEmail email, CancellationToken cancellationToken)
    {
        try
        {
            using var mail = CreateMessage(email);
            using var client = new SmtpClient(options.Host, options.Port)
            {
                EnableSsl = options.EnableSsl,
                UseDefaultCredentials = false,
            };
            if (!string.IsNullOrWhiteSpace(options.UserName))
                client.Credentials = new NetworkCredential(options.UserName, options.Password);
            await client.SendMailAsync(mail, cancellationToken);
            return OperationResultFactory.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FormatException) { return Failure(HelloEmailTransportErrorCodes.Permanent); }
        catch (SmtpException exception) when ((int)exception.StatusCode >= 500 && (int)exception.StatusCode < 600)
        { return Failure(HelloEmailTransportErrorCodes.Permanent); }
        catch (Exception) { return Failure(HelloEmailTransportErrorCodes.Retryable); }
    }

    internal static MailMessage CreateMessage(HelloEmail email)
    {
        var mail = new MailMessage
        {
            From = new MailAddress(email.FromAddress, email.FromName),
            To = { new MailAddress(email.RecipientAddress) },
            Subject = email.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = email.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            email.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        return mail;
    }

    internal static OperationResult Failure(string code) => OperationResultFactory.Fail(
        new Error(code, "The account email could not be delivered.", ErrorType.Failure));
}
