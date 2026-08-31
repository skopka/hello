using Skopka.Hello.AuthorizationServer;

namespace Skopka.Hello.Tests;

public sealed class HelloAuthorizationServerOptionsTests
{
    [Fact]
    public void ReferenceAccessTokensRemainTheDefault()
    {
        var options = CreateOptions();

        Assert.Equal(
            HelloAuthorizationAccessTokenFormat.Reference,
            options.AccessTokenFormat);
        options.Validate();
    }

    [Fact]
    public void AdditionalScopeAndPerClientResourceAreSupported()
    {
        var options = CreateOptions();
        options.AccessTokenFormat =
            HelloAuthorizationAccessTokenFormat.SelfContainedJwt;
        options.AdditionalScopes.Add("mail");
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "roundcube",
            DisplayName = "Roundcube",
            Type = HelloAuthorizationClientType.Confidential,
            ClientSecret = "secret",
            Resource = "stalwart",
            RedirectUris =
            [
                "https://webmail.example.test/index.php/login/oauth",
            ],
            Scopes =
            [
                "openid",
                "offline_access",
                "email",
                "mail",
            ],
        });

        options.Validate();
    }

    [Theory]
    [InlineData("openid")]
    [InlineData("mail scope")]
    [InlineData("mail\\scope")]
    [InlineData("mail\"scope")]
    public void AdditionalScopeRejectsReservedOrInvalidName(string scope)
    {
        var options = CreateOptions();
        options.AdditionalScopes.Add(scope);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void AdditionalScopesRejectDuplicatesAndExcessiveCount()
    {
        var duplicated = CreateOptions();
        duplicated.AdditionalScopes.AddRange(["mail", "mail"]);
        Assert.Throws<InvalidOperationException>(duplicated.Validate);

        var excessive = CreateOptions();
        excessive.AdditionalScopes.AddRange(
            Enumerable.Range(0, 33).Select(index => $"scope-{index}"));
        Assert.Throws<InvalidOperationException>(excessive.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("stalwart mail")]
    public void ClientRejectsInvalidResource(string resource)
    {
        var options = CreateOptions();
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "roundcube",
            DisplayName = "Roundcube",
            Type = HelloAuthorizationClientType.Confidential,
            ClientSecret = "secret",
            Resource = resource,
            RedirectUris = ["https://webmail.example.test/oauth"],
            Scopes = ["openid"],
        });

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

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
    public void ClientSupportsPostLogoutRedirects()
    {
        var options = CreateOptions();
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "bff",
            DisplayName = "BFF",
            Type = HelloAuthorizationClientType.Confidential,
            ClientSecret = "secret",
            RedirectUris = ["https://bff.example.test/signin-oidc"],
            PostLogoutRedirectUris =
            [
                "https://bff.example.test/signed-out",
            ],
            Scopes = ["openid"],
        });

        options.Validate();
    }

    [Theory]
    [InlineData("http://bff.example.test/signed-out")]
    [InlineData("https://user@bff.example.test/signed-out")]
    [InlineData("https://bff.example.test/signed-out#fragment")]
    public void ClientRejectsUnsafePostLogoutRedirect(string redirectUri)
    {
        var options = CreateOptions();
        options.Clients.Add(new HelloAuthorizationClientOptions
        {
            ClientId = "bff",
            DisplayName = "BFF",
            Type = HelloAuthorizationClientType.Confidential,
            ClientSecret = "secret",
            RedirectUris = ["https://bff.example.test/signin-oidc"],
            PostLogoutRedirectUris = [redirectUri],
            Scopes = ["openid"],
        });

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void EndpointPathsMustBeDistinct()
    {
        var options = CreateOptions();
        options.EndSessionEndpointPath = options.TokenEndpointPath;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void AccountSelectionIsOptInAndUsesThePackagedChooserPath()
    {
        var options = CreateOptions();

        Assert.False(options.AccountSelectionEnabled);
        Assert.Equal("/hello/accounts", options.AccountSelectionPath);
        options.Validate();
    }

    [Fact]
    public void AccountSelectionRejectsAnEndpointCollision()
    {
        var options = CreateOptions();
        options.AccountSelectionEnabled = true;
        options.AccountSelectionPath = options.AuthorizationEndpointPath;

        Assert.Throws<InvalidOperationException>(options.Validate);
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
