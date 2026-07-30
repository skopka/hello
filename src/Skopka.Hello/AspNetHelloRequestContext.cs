using Microsoft.AspNetCore.Http;
using Skopka.Identity.Sessions;

namespace Skopka.Hello;

public sealed class AspNetHelloRequestContext : IHelloRequestContext
{
    public string? CreateClientKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var address = httpContext.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.ToString();
    }

    public IdentitySessionMetadata CreateSessionMetadata(
        HttpContext httpContext,
        string clientName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);

        var boundedClientName = Bound(
            clientName,
            SessionLimits.MaximumClientNameLength);
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var deviceName = string.IsNullOrWhiteSpace(userAgent)
            ? null
            : Bound(
                RemoveControlCharacters(userAgent),
                SessionLimits.MaximumDeviceNameLength);

        return new IdentitySessionMetadata(
            boundedClientName,
            deviceName);
    }

    private static string Bound(string value, int maximumLength)
        => value.Length <= maximumLength
            ? value
            : value[..maximumLength];

    private static string RemoveControlCharacters(string value)
        => string.Create(
            value.Length,
            value,
            static (span, source) =>
            {
                var destination = 0;
                foreach (var character in source)
                {
                    if (!char.IsControl(character))
                    {
                        span[destination++] = character;
                    }
                }

                span[destination..].Fill(' ');
            }).TrimEnd();
}
