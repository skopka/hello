using Skopka.Hello.AuthorizationServer;

namespace Skopka.Hello.Tests;

public sealed class HelloAuthorizationServerOptionsTests
{
    [Fact]
    public void PublicClientRequiresPkceSafeRedirectWithoutSecret()
    {
        var options = CreateOptions();
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "native",
            DisplayName = "Native",
            Type = HelloAuthorizationClientType.Public,
            ClientSecret = "not-allowed",
            RedirectUris = ["com.example.app:/callback"],
            Scopes = ["openid"],
        });

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            "cannot have a secret",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConfidentialClientRequiresSecret()
    {
        var options = CreateOptions();
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "bff",
            DisplayName = "BFF",
            Type = HelloAuthorizationClientType.Confidential,
            RedirectUris = ["https://bff.example.test/signin-oidc"],
            Scopes = ["openid"],
        });

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            "requires a secret",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://client.example.test/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user@client.example.test/callback")]
    [InlineData("https://client.example.test/callback#fragment")]
    public void ClientRejectsUnsafeRedirect(string redirectUri)
    {
        var options = CreateOptions();
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "native",
            DisplayName = "Native",
            Type = HelloAuthorizationClientType.Public,
            RedirectUris = [redirectUri],
            Scopes = ["openid"],
        });

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void SupportsLoopbackAndReverseDomainNativeRedirects()
    {
        var options = CreateOptions();
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "native",
            DisplayName = "Native",
            Type = HelloAuthorizationClientType.Public,
            RedirectUris =
            [
                "http://127.0.0.1:49152/callback",
                "com.example.app:/callback",
            ],
            Scopes = ["openid", "offline_access", "profile"],
        });

        options.Validate();
    }

    [Fact]
    public void ProductionIssuerMustUseHttps()
    {
        var options = CreateOptions();
        options.Issuer = new Uri("http://hello.example.test");

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static HelloAuthorizationServerOptions CreateOptions()
        => new()
        {
            Issuer = new Uri("https://hello.example.test"),
        };
}
