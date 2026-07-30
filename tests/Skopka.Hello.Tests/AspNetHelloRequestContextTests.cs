using System.Net;
using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.Tests;

public sealed class AspNetHelloRequestContextTests
{
    [Fact]
    public void CreateClientKeyUsesServerConnectionAddress()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress =
            IPAddress.Parse("::ffff:192.0.2.10");
        var context = new AspNetHelloRequestContext();

        var key = context.CreateClientKey(httpContext);

        Assert.Equal("192.0.2.10", key);
    }

    [Fact]
    public void CreateSessionMetadataDoesNotIncludeRemoteAddress()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress =
            IPAddress.Parse("192.0.2.10");
        httpContext.Request.Headers.UserAgent =
            "Browser/1.0\r\nInjected";
        var context = new AspNetHelloRequestContext();

        var metadata = context.CreateSessionMetadata(
            httpContext,
            "test-client");

        Assert.Equal("test-client", metadata.ClientName);
        Assert.DoesNotContain(
            "192.0.2.10",
            metadata.DeviceName,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\r",
            metadata.DeviceName,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\n",
            metadata.DeviceName,
            StringComparison.Ordinal);
    }
}
