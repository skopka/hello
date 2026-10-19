using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

/// <summary>
/// Test provider. Construction fails outside Development so an accidental
/// production registration cannot print one-time codes.
/// </summary>
public sealed class DevelopmentLoggingHelloSmsProvider(
    IHostEnvironment environment,
    ILogger<DevelopmentLoggingHelloSmsProvider> logger,
    HelloPhoneSmsTemplates templates)
    : IHelloAccountMessageProvider
{
    private static readonly Action<ILogger, string, Exception?> LogSms =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3001, "DevelopmentSms"),
            "Development SMS: {Message}");

    private static readonly Action<ILogger, HelloAccountMessageKind,
        string, Exception?> LogCode =
        LoggerMessage.Define<HelloAccountMessageKind, string>(
            LogLevel.Warning,
            new EventId(3002, "DevelopmentSmsCode"),
            "Development SMS ({Kind}) code: {Code}");

    public const string Id = "development-log";

    public string ProviderId => Id;

    public HelloDeliveryChannel Channel => HelloDeliveryChannel.Sms;

    public Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "The logging SMS provider is available only in Development.");
        }

        if (message.Kind == HelloAccountMessageKind.PhoneSignInVerification
            && message.VerificationCode is { } code)
        {
            LogSms(logger,
                templates.Render(code, message.TemplateVariant), null);
        }
        else if (message.VerificationCode is { } otherCode)
        {
            LogCode(logger, message.Kind, otherCode, null);
        }
        else
        {
            throw new InvalidOperationException(
                "The development SMS provider requires a verification code.");
        }

        return Task.FromResult(OperationResultFactory.Success());
    }
}
