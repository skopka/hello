# Email transport

Hello renders account emails and routes them through the configured email provider. SMTP is the default transport. A host can replace only the final delivery step while retaining the account-message templates, localization, routing and queue:

```csharp
services.AddSkopkaHelloEmailTransport<MyEmailTransport>();
```

Register the email provider and sender address with `AddSkopkaHelloSmtpProvider` as well; its localization and queue settings remain in use. A custom transport does not require an SMTP host. Configure the email provider ID in `AddSkopkaHelloDelivery` as usual. The custom transport receives a rendered `HelloEmail` containing the sender, recipient, subject, plain-text and HTML bodies, and message kind. It must not log those fields: bodies can contain action URLs and one-time codes. Keep provider credentials in host configuration or a secret store.

For example, an adapter for a hypothetical JSON email service can use an `HttpClient` supplied by the host. Adapt the endpoint, authentication and status mapping to the actual service's contract:

```csharp
using System.Net;
using System.Net.Http.Json;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;

// Configure the client, including its base address and authorization header,
// from host configuration. Do not put API keys in source code.
services.AddHttpClient("mail-api", client =>
{
    client.BaseAddress = new Uri(configuration["MailApi:BaseUrl"]!);
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", configuration["MailApi:ApiKey"]);
});
services.AddSkopkaHelloEmailTransport<MyEmailTransport>();

public sealed class MyEmailTransport(IHttpClientFactory clients) : IHelloEmailTransport
{
    public async Task<OperationResult> SendAsync(
        HelloEmail email, CancellationToken cancellationToken)
    {
        try
        {
            var client = clients.CreateClient("mail-api");
            using var response = await client.PostAsJsonAsync("messages", new
            {
                from = new { address = email.FromAddress, name = email.FromName },
                to = email.RecipientAddress,
                subject = email.Subject,
                text = email.TextBody,
                html = email.HtmlBody,
            }, cancellationToken);

            if (response.IsSuccessStatusCode)
                return OperationResultFactory.Success();

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500;
            return OperationResultFactory.Fail(new Error(
                retryable ? HelloEmailTransportErrorCodes.Retryable
                          : HelloEmailTransportErrorCodes.Permanent,
                "Email delivery failed.", ErrorType.Failure));
        }
        catch (HttpRequestException)
        {
            return OperationResultFactory.Fail(new Error(
                HelloEmailTransportErrorCodes.Retryable,
                "Email delivery failed.", ErrorType.Failure));
        }
    }
}
```

Success means the provider accepted the message; it does not guarantee final inbox delivery. Return `Retryable` for temporary failures such as throttling or an unavailable service, and `Permanent` for failures that cannot succeed by retrying unchanged. Other failure codes and exceptions are treated as retryable. Pass the cancellation token to all I/O and allow cancellation to propagate. Do not include addresses, message contents, credentials, provider response bodies, or provider tokens in errors or logs.

The renderer uses the host-level email localization default for every message, including background work. It does not choose the recipient's UI language. English and Russian are packaged, and a host can override or add dictionaries using `HelloSmtpOptions.Localization`; see [getting started](getting-started.md) for an example.

Transports are resolved in a dependency injection scope for delivery, so scoped dependencies can be used. The reusable provider's bounded in-memory background queue is not durable across process restarts. The ready Server uses its PostgreSQL outbox for configured email delivery and invokes the transport from the worker; delivery is at least once, so a provider may receive duplicates after a crash or timeout. The in-memory worker retries temporary failures up to three total attempts, with one- and two-second delays bounded by message expiration; it drops permanent failures immediately. The durable outbox uses its configured retry policy and retains permanent failures as terminal failures. Keep the Data Protection key ring available while durable records remain queued or retained. See [deployment](deployment.md) and [getting started](getting-started.md) for configuration and operational details.
