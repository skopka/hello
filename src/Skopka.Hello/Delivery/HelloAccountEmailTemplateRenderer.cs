using System.Net;

namespace Skopka.Hello;

internal sealed class HelloAccountEmailTemplateRenderer(
    HelloAccountEmailTextCatalog catalog)
{
    public HelloAccountEmailContent Render(HelloAccountMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var keys = GetKeys(message.Kind, message.TemplateVariant);
        var subject = catalog.GetRequiredString(keys.Subject);
        var introduction = catalog.GetRequiredString(
            keys.Introduction);
        var expires = message.ExpiresAt
            .ToUniversalTime()
            .ToString("g", catalog.Culture);
        var ignore = catalog.GetRequiredString(
            HelloAccountEmailTextKeys.IgnoreUnrequested);

        return keys.Action is not null
            ? RenderAction(
                message,
                subject,
                introduction,
                catalog.GetRequiredString(keys.Action),
                Format(
                    HelloAccountEmailTextKeys.LinkExpires,
                    expires),
                ignore)
            : RenderVerification(
                message,
                subject,
                introduction,
                Format(
                    HelloAccountEmailTextKeys.CodeExpires,
                    expires),
                ignore);
    }

    internal static HelloAccountEmailTemplateKeys GetKeys(
        HelloAccountMessageKind kind,
        string? templateVariant = null)
        => kind switch
        {
            HelloAccountMessageKind.PasswordReset => new(
                HelloAccountEmailTextKeys.PasswordResetSubject,
                HelloAccountEmailTextKeys.PasswordResetIntroduction,
                HelloAccountEmailTextKeys.PasswordResetAction),
            HelloAccountMessageKind.EmailConfirmation => new(
                HelloAccountEmailTextKeys.EmailConfirmationSubject,
                HelloAccountEmailTextKeys.EmailConfirmationIntroduction,
                HelloAccountEmailTextKeys.EmailConfirmationAction),
            HelloAccountMessageKind.PhoneConfirmation => new(
                HelloAccountEmailTextKeys.PhoneConfirmationSubject,
                HelloAccountEmailTextKeys.PhoneConfirmationIntroduction,
                HelloAccountEmailTextKeys.PhoneConfirmationAction),
            HelloAccountMessageKind.PasswordChangeVerification => new(
                HelloAccountEmailTextKeys.PasswordChangeVerificationSubject,
                HelloAccountEmailTextKeys.PasswordChangeVerificationIntroduction),
            HelloAccountMessageKind.ExternalLoginLinkVerification => new(
                HelloAccountEmailTextKeys.ExternalLoginLinkVerificationSubject,
                HelloAccountEmailTextKeys.ExternalLoginLinkVerificationIntroduction),
            HelloAccountMessageKind.ExternalLoginUnlinkVerification => new(
                HelloAccountEmailTextKeys.ExternalLoginUnlinkVerificationSubject,
                HelloAccountEmailTextKeys.ExternalLoginUnlinkVerificationIntroduction),
            HelloAccountMessageKind.AccountSecurityVerification =>
                GetAccountSecurityKeys(templateVariant),
            HelloAccountMessageKind.AdminActionVerification => new(
                HelloAccountEmailTextKeys.AdminActionVerificationSubject,
                HelloAccountEmailTextKeys.AdminActionVerificationIntroduction),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The account-message kind is unsupported."),
        };

    private static HelloAccountEmailTemplateKeys GetAccountSecurityKeys(
        string? templateVariant)
        => templateVariant switch
        {
            HelloAccountEmailTemplateVariants.PasswordSet => new(
                HelloAccountEmailTextKeys.PasswordSetVerificationSubject,
                HelloAccountEmailTextKeys.PasswordSetVerificationIntroduction),
            HelloAccountEmailTemplateVariants.PasswordRemove => new(
                HelloAccountEmailTextKeys.PasswordRemoveVerificationSubject,
                HelloAccountEmailTextKeys.PasswordRemoveVerificationIntroduction),
            HelloAccountEmailTemplateVariants.AccountDelete => new(
                HelloAccountEmailTextKeys.AccountDeleteVerificationSubject,
                HelloAccountEmailTextKeys.AccountDeleteVerificationIntroduction),
            HelloAccountEmailTemplateVariants.AuthenticatorDisable => new(
                HelloAccountEmailTextKeys.AuthenticatorDisableVerificationSubject,
                HelloAccountEmailTextKeys.AuthenticatorDisableVerificationIntroduction),
            _ => new(
                HelloAccountEmailTextKeys.AccountSecurityVerificationSubject,
                HelloAccountEmailTextKeys.AccountSecurityVerificationIntroduction),
        };

    private static HelloAccountEmailContent RenderAction(
        HelloAccountMessage message,
        string subject,
        string introduction,
        string action,
        string expiry,
        string ignore)
    {
        if (message.ActionUrl is null)
        {
            throw new InvalidOperationException(
                "An action URL is required for this account message.");
        }

        var url = message.ActionUrl.AbsoluteUri;
        return new HelloAccountEmailContent(
            subject,
            $"""
            {introduction}

            {action}: {url}

            {expiry}
            {ignore}
            """,
            $"""
            <p>{Encode(introduction)}</p>
            <p><a href="{Encode(url)}">{Encode(action)}</a></p>
            <p>{Encode(expiry)}</p>
            <p>{Encode(ignore)}</p>
            """);
    }

    private static HelloAccountEmailContent RenderVerification(
        HelloAccountMessage message,
        string subject,
        string introduction,
        string expiry,
        string ignore)
    {
        if (string.IsNullOrWhiteSpace(message.VerificationCode))
        {
            throw new InvalidOperationException(
                "A verification code is required for this account message.");
        }

        return new HelloAccountEmailContent(
            subject,
            $"""
            {introduction}

            {message.VerificationCode}

            {expiry}
            {ignore}
            """,
            $"""
            <p>{Encode(introduction)}</p>
            <p><strong>{Encode(message.VerificationCode)}</strong></p>
            <p>{Encode(expiry)}</p>
            <p>{Encode(ignore)}</p>
            """);
    }

    private string Format(string key, string argument)
    {
        var template = catalog.GetRequiredString(key);
        try
        {
            return String.Format(catalog.Culture, template, argument);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"The account-message text '{key}' has an invalid format string.",
                exception);
        }
    }

    private static string Encode(string value)
        => WebUtility.HtmlEncode(value);
}

internal sealed record HelloAccountEmailContent(
    string Subject,
    string TextBody,
    string HtmlBody);

internal sealed record HelloAccountEmailTemplateKeys(
    string Subject,
    string Introduction,
    string? Action = null);
