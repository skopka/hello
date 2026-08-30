using Microsoft.AspNetCore.Http;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloUiErrorHandlingTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("*/*", false)]
    [InlineData("application/json", false)]
    [InlineData("application/problem+json", false)]
    [InlineData("text/html", true)]
    [InlineData("application/xhtml+xml", true)]
    [InlineData("text/html, application/json", true)]
    [InlineData("application/json, text/html;q=0.5", false)]
    public void PrefersHtmlNegotiatesExplicitMediaTypes(
        string? accept,
        bool expected)
    {
        var context = new DefaultHttpContext();
        if (accept is not null)
        {
            context.Request.Headers.Accept = accept;
        }

        Assert.Equal(
            expected,
            HelloUiRequestNegotiation.PrefersHtml(context));
    }

    [Fact]
    public void BrowserNavigationPrefersHtml()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Sec-Fetch-Mode"] = "navigate";
        context.Request.Headers.Accept = "application/json";

        Assert.True(HelloUiRequestNegotiation.PrefersHtml(context));
    }
}
