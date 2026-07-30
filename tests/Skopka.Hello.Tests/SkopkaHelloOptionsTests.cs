namespace Skopka.Hello.Tests;

public sealed class SkopkaHelloOptionsTests
{
    [Fact]
    public void ValidateRejectsInsecureHostCookie()
    {
        var options = new SkopkaHelloOptions
        {
            SecureCookies = false,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            "__Host-",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAllowsExplicitNonHostDevelopmentCookie()
    {
        var options = new SkopkaHelloOptions
        {
            SecureCookies = false,
            RefreshCookieName = "Skopka.Hello.Refresh",
            AntiforgeryCookieName = "Skopka.Hello.Antiforgery",
            AntiforgeryRequestCookieName =
                "Skopka.Hello.XSRF-TOKEN",
        };

        options.Validate();
    }

    [Fact]
    public void ValidateAllowsPublicHttpOriginWithoutPath()
    {
        var options = new SkopkaHelloOptions
        {
            PublicOrigin = new Uri("https://accounts.example.test"),
        };

        options.Validate();
    }

    [Theory]
    [InlineData("https://accounts.example.test/hello")]
    [InlineData("https://user@example.test/")]
    [InlineData("ftp://accounts.example.test/")]
    public void ValidateRejectsUnsafePublicOrigin(string value)
    {
        var options = new SkopkaHelloOptions
        {
            PublicOrigin = new Uri(value),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
