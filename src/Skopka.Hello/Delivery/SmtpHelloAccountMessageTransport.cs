using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
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
        var content = message.Kind switch
        {
            HelloAccountMessageKind.PasswordReset => CreateActionContent(
                message,
                "Reset your password",
                "A password reset was requested for your account.",
                "Reset password"),
            HelloAccountMessageKind.EmailConfirmation => CreateActionContent(
                message,
                "Confirm your email address",
                "Confirm the email address for your account.",
                "Confirm email"),
            HelloAccountMessageKind.PasswordChangeVerification =>
                CreateVerificationContent(
                    message,
                    "Confirm your password change",
                    "Use this verification code to change your password:"),
            HelloAccountMessageKind.ExternalLoginLinkVerification =>
                CreateVerificationContent(
                    message,
                    "Confirm external sign-in linking",
                    "Use this verification code to link an external sign-in provider:"),
            HelloAccountMessageKind.ExternalLoginUnlinkVerification =>
                CreateVerificationContent(
                    message,
                    "Confirm external sign-in removal",
                    "Use this verification code to remove an external sign-in provider:"),
            HelloAccountMessageKind.AccountSecurityVerification =>
                CreateVerificationContent(
                    message,
                    "Confirm account security action",
                    "Use this verification code to authorize the requested account security action:"),
            HelloAccountMessageKind.AdminActionVerification =>
                CreateVerificationContent(
                    message,
                    "Confirm administrative action",
                    "Use this verification code to authorize the requested administrative action:"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(message),
                message.Kind,
                "The SMTP account message kind is unsupported."),
        };

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

    private static EmailContent CreateActionContent(
        HelloAccountMessage message,
        string subject,
        string introduction,
        string linkText)
    {
        if (message.ActionUrl is null)
        {
            throw new InvalidOperationException(
                "An action URL is required for this account message.");
        }

        var url = message.ActionUrl.AbsoluteUri;
        var expires = message.ExpiresAt
            .ToUniversalTime()
            .ToString("u");
        return new EmailContent(
            subject,
            $"""
            {introduction}

            {linkText}: {url}

            This link expires at {expires}.
            If you did not request this action, ignore this message.
            """,
            $"""
            <p>{WebUtility.HtmlEncode(introduction)}</p>
            <p><a href="{WebUtility.HtmlEncode(url)}">{WebUtility.HtmlEncode(linkText)}</a></p>
            <p>This link expires at {WebUtility.HtmlEncode(expires)}.</p>
            <p>If you did not request this action, ignore this message.</p>
            """);
    }

    private static EmailContent CreateVerificationContent(
        HelloAccountMessage message,
        string subject,
        string introduction)
    {
        if (string.IsNullOrWhiteSpace(message.VerificationCode))
        {
            throw new InvalidOperationException(
                "A verification code is required for this account message.");
        }

        var expires = message.ExpiresAt
            .ToUniversalTime()
            .ToString("u");
        return new EmailContent(
            subject,
            $"""
            {introduction}

            {message.VerificationCode}

            This code expires at {expires}.
            If you did not request this action, ignore this message.
            """,
            $"""
            <p>{WebUtility.HtmlEncode(introduction)}</p>
            <p><strong>{WebUtility.HtmlEncode(message.VerificationCode)}</strong></p>
            <p>This code expires at {WebUtility.HtmlEncode(expires)}.</p>
            <p>If you did not request this action, ignore this message.</p>
            """);
    }

    private static OperationResult DeliveryFailed()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.Failed,
                "The account message could not be delivered.",
                ErrorType.Failure));

    private sealed record EmailContent(
        string Subject,
        string TextBody,
        string HtmlBody);
}
