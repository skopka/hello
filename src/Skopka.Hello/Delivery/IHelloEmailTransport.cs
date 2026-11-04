using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

/// <summary>A fully rendered account email. Its contents may contain secrets.</summary>
public sealed record HelloEmail(
    string FromAddress,
    string FromName,
    string RecipientAddress,
    string Subject,
    string TextBody,
    string HtmlBody,
    HelloAccountMessageKind Kind)
{
    public override string ToString() => "HelloEmail { contents redacted }";
}

/// <summary>Delivers a rendered email. Implementations must not log its contents.</summary>
public interface IHelloEmailTransport
{
    Task<OperationResult> SendAsync(HelloEmail email, CancellationToken cancellationToken);
}

/// <summary>Failure codes understood by Hello's email delivery queues.</summary>
public static class HelloEmailTransportErrorCodes
{
    public const string Retryable = "hello.email.retryable";
    public const string Permanent = "hello.email.permanent";
}
