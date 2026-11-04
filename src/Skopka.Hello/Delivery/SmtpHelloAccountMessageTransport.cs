using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class SmtpHelloAccountMessageTransport
{
    private readonly HelloSmtpOptions options;
    private readonly HelloAccountEmailTemplateRenderer renderer;
    private readonly IServiceScopeFactory? scopeFactory;

    public SmtpHelloAccountMessageTransport(HelloSmtpOptions options, HelloAccountEmailTemplateRenderer renderer)
        : this(options, renderer, null) { }

    public SmtpHelloAccountMessageTransport(HelloSmtpOptions options, HelloAccountEmailTemplateRenderer renderer, IServiceScopeFactory? scopeFactory)
        => (this.options, this.renderer, this.scopeFactory) = (options, renderer, scopeFactory);

    public async Task<OperationResult> SendAsync(HelloAccountMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (message.Channel != HelloDeliveryChannel.Email)
            return OperationResultFactory.Fail(HelloAccountMessageValidator.ChannelMismatch());
        var validation = HelloAccountMessageValidator.Validate(message, DateTimeOffset.UtcNow);
        if (validation is not null) return OperationResultFactory.Fail(validation);
        try
        {
            var email = Render(message);
            OperationResult result;
            if (scopeFactory is null)
                result = await new SmtpHelloEmailTransport(options).SendAsync(email, cancellationToken);
            else
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                result = await scope.ServiceProvider.GetRequiredService<IHelloEmailTransport>().SendAsync(email, cancellationToken);
            }
            if (result.IsSuccess) return result;
            // Never propagate arbitrary provider strings into logs or durable journals.
            return SmtpHelloEmailTransport.Failure(result.Errors.Any(e => e.Code == HelloEmailTransportErrorCodes.Permanent)
                ? HelloEmailTransportErrorCodes.Permanent : HelloEmailTransportErrorCodes.Retryable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return SmtpHelloEmailTransport.Failure(HelloEmailTransportErrorCodes.Retryable); }
    }

    private HelloEmail Render(HelloAccountMessage message)
    {
        var content = renderer.Render(message);
        return new HelloEmail(options.FromAddress, options.FromName, message.RecipientAddress,
            content.Subject, content.TextBody, content.HtmlBody, message.Kind);
    }

    internal MailMessage CreateMessage(HelloAccountMessage message)
        => SmtpHelloEmailTransport.CreateMessage(Render(message));
}
