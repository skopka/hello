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
}
