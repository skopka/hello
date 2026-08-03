namespace Skopka.Hello.Tests;

public sealed class SkopkaHelloOptionsTests
{
    [Fact]
    public void DefaultsPreserveExistingRegistrationAndUiRoutes()
    {
        var options = new SkopkaHelloOptions();

        options.Validate();

        Assert.True(options.SelfRegistrationEnabled);
        Assert.Equal("/hello", options.UiPathPrefix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("hello")]
    [InlineData("/hello/")]
    [InlineData("/hello//accounts")]
    [InlineData("/hello/../accounts")]
    [InlineData("/hello?tenant=one")]
    [InlineData("/hello/{tenant}")]
    [InlineData("/hello%2Faccounts")]
    [InlineData("/auth")]
    [InlineData("/AUTH/custom")]
    [InlineData("/account/sessions")]
    [InlineData("/health/live")]
    [InlineData("/swagger")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/_content/custom")]
    [InlineData("/signin-skopka-oidc/google")]
    public void ValidateRejectsUnsafeUiPathPrefix(string value)
    {
        var options = new SkopkaHelloOptions
        {
            UiPathPrefix = value,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("/identity")]
    [InlineData("/platform/identity")]
    public void ValidateAllowsSupportedUiPathPrefix(string value)
    {
        var options = new SkopkaHelloOptions
        {
            UiPathPrefix = value,
        };

        options.Validate();
    }

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
