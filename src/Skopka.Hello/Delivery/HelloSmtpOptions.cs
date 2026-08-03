using System.Net.Mail;

namespace Skopka.Hello;

public sealed class HelloSmtpOptions
{
    public string ProviderId { get; set; } = "smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Skopka.Hello";

    public int QueueCapacity { get; set; } = 256;

    public void Validate()
    {
        _ = HelloAccountMessageDispatcher.NormalizeProviderId(
            ProviderId,
            "The SMTP provider id");
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(FromAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(FromName);

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                "The SMTP port must be between 1 and 65535.");
        }

        if (QueueCapacity is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                "The SMTP queue capacity must be between 1 and 10000.");
        }

        if (string.IsNullOrWhiteSpace(UserName)
            != string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException(
                "SMTP user name and password must be configured together.");
        }

        try
        {
            _ = new MailAddress(FromAddress);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "The SMTP from address is invalid.",
                exception);
        }
    }
}
