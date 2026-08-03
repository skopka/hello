using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.Tests;

public sealed class HelloOidcChallengeServiceTests
{
    [Fact]
    public void SignInPreservesLocalReturnUrlAndBindsCanonicalProvider()
    {
        using var services = CreateServices();
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();

        var result = challenges.CreateSignIn(
            "GitHub",
            "/hello/account?tab=sessions");

        Assert.True(result.IsSuccess);
        var challenge = result.Value;
        Assert.Equal(
            HelloOidcDefaults.ProviderSchemePrefix + "github",
            challenge.AuthenticationScheme);
        Assert.Equal(
            HelloOidcDefaults.CompletionPath,
            challenge.Properties.RedirectUri);
        Assert.False(challenge.Properties.AllowRefresh);
        Assert.False(challenge.Properties.IsPersistent);
        Assert.Equal(
            "sign_in",
            challenge.Properties.Items["hello:oidc:intent"]);
        Assert.Equal(
            "github",
            challenge.Properties.Items["hello:oidc:provider"]);
        Assert.Equal(
            "/hello/account?tab=sessions",
            challenge.Properties.Items["hello:oidc:return_url"]);
        Assert.True(Guid.TryParse(
            challenge.Properties.Items["hello:oidc:flow_id"],
            out var flowId));
        Assert.NotEqual(Guid.Empty, flowId);
        Assert.False(
            challenge.Properties.Items.ContainsKey(
                "hello:oidc:user_id"));
        Assert.False(
            challenge.Properties.Items.ContainsKey(
                "hello:oidc:session_id"));
    }

    [Fact]
    public void EachChallengeGetsANewFlowId()
    {
        using var services = CreateServices();
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();

        var first = challenges.CreateSignIn("github", null);
        var second = challenges.CreateSignIn("github", null);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(
            first.Value.Properties.Items["hello:oidc:flow_id"],
            second.Value.Properties.Items["hello:oidc:flow_id"]);
    }

    [Fact]
    public void CustomUiPrefixControlsOidcBrowserRoutes()
    {
        using var services = CreateServices("/identity");
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();

        var signIn = challenges.CreateSignIn(
            "github",
            "/identity/external/complete?unsafe=true");
        var link = challenges.CreateLink(
            "github",
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.True(signIn.IsSuccess);
        Assert.True(link.IsSuccess);
        Assert.Equal(
            "/identity/external/complete",
            signIn.Value.Properties.RedirectUri);
        Assert.Equal(
            "/identity/account",
            signIn.Value.Properties.Items[
                "hello:oidc:return_url"]);
        Assert.Equal(
            "/identity/account/external-logins",
            link.Value.Properties.Items[
                "hello:oidc:return_url"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example.test/steal")]
    [InlineData("//evil.example.test/steal")]
    [InlineData("/\\evil.example.test/steal")]
    [InlineData("/signin-skopka-oidc/github")]
    [InlineData("/hello/external/complete?next=/hello/account")]
    public void SignInReplacesUnsafeReturnUrl(string? returnUrl)
    {
        using var services = CreateServices();
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();

        var result = challenges.CreateSignIn("github", returnUrl);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "/hello/account",
            result.Value.Properties.Items[
                "hello:oidc:return_url"]);
    }

    [Fact]
    public void SignInReplacesOversizedLocalReturnUrl()
    {
        using var services = CreateServices();
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();

        var result = challenges.CreateSignIn(
            "github",
            "/" + new string('a', 2_048));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "/hello/account",
            result.Value.Properties.Items[
                "hello:oidc:return_url"]);
    }

    [Fact]
    public void LinkBindsProviderUserAndSessionToChallenge()
    {
        using var services = CreateServices();
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var result = challenges.CreateLink(
            "GITHUB",
            userId,
            sessionId);

        Assert.True(result.IsSuccess);
        var items = result.Value.Properties.Items;
        Assert.Equal("link", items["hello:oidc:intent"]);
        Assert.Equal("github", items["hello:oidc:provider"]);
        Assert.Equal(
            HelloOidcDefaults.ExternalLoginsPath,
            items["hello:oidc:return_url"]);
        Assert.Equal(
            userId.ToString("D"),
            items["hello:oidc:user_id"]);
        Assert.Equal(
            sessionId.ToString("D"),
            items["hello:oidc:session_id"]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void LinkRejectsEmptyUserOrSession(
        bool emptyUser,
        bool emptySession)
    {
        using var services = CreateServices();
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();

        var result = challenges.CreateLink(
            "github",
            emptyUser ? Guid.Empty : Guid.NewGuid(),
            emptySession ? Guid.Empty : Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == "hello.oidc.pending_identity_invalid");
    }

    [Fact]
    public void UnknownProviderCannotCreateChallenge()
    {
        using var services = CreateServices();
        var challenges = services.GetRequiredService<
            IHelloOidcChallengeService>();

        var result = challenges.CreateSignIn(
            "unknown",
            "/hello/account");

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == "hello.oidc.provider_unavailable");
    }

    private static ServiceProvider CreateServices(
        string uiPathPrefix = "/hello")
    {
        var services = new ServiceCollection();
        services.AddSkopkaHello<object>(options =>
            options.UiPathPrefix = uiPathPrefix);
        services.AddSkopkaHelloOidc<object>(options =>
        {
            options.PublicOrigin = new Uri(
                "https://hello.example.test");
            options.Providers["GitHub"] = new HelloOidcProviderOptions
            {
                DisplayName = "GitHub",
                Authority = "https://accounts.example.test",
                ClientId = "hello-tests",
                ClientSecret = "not-a-production-secret",
            };
        });
        return services.BuildServiceProvider();
    }
}
