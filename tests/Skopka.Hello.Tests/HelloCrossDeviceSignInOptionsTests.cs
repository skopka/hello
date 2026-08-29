using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Skopka.Identity.DeviceAuthorization;

namespace Skopka.Hello.Tests;

public sealed class HelloCrossDeviceSignInOptionsTests
{
    [Fact]
    public void FeatureIsDisabledByDefault()
    {
        var services = new ServiceCollection();
        services.AddSkopkaHello<TestProfile>();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<
            HelloCrossDeviceSignInOptions>();

        Assert.False(options.Enabled);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(
                IHelloCrossDeviceSignInApplication<TestProfile>));
    }

    [Fact]
    public void ExtensionEnablesMatchingHelloAndIdentityOptions()
    {
        var services = new ServiceCollection();
        services.AddSkopkaHello<TestProfile>(options =>
        {
            options.PublicOrigin = new Uri(
                "https://accounts.example.test");
            options.Totp.Enabled = true;
        })
            .AddCrossDeviceSignIn(options =>
            {
                options.RequestLifetime = TimeSpan.FromMinutes(3);
                options.PollingInterval = TimeSpan.FromSeconds(4);
                options.UserCodeLength = 10;
                options.UserCodeGroupSize = 5;
            });

        using var provider = services.BuildServiceProvider();
        var hello = provider.GetRequiredService<
            HelloCrossDeviceSignInOptions>();
        var identity = provider.GetRequiredService<
            DeviceAuthorizationOptions>();

        Assert.True(hello.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(3), identity.RequestLifetime);
        Assert.Equal(10, identity.UserCodeLength);
        Assert.Equal(5, identity.UserCodeGroupSize);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(
                IHelloCrossDeviceSignInApplication<TestProfile>));
    }

    [Fact]
    public void ExplicitlyDisabledExtensionNeedsNoAdditionalServices()
    {
        var services = new ServiceCollection();
        services.AddSkopkaHello<TestProfile>()
            .AddCrossDeviceSignIn(options => options.Enabled = false);

        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<
            HelloCrossDeviceSignInOptions>().Enabled);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(
                IHelloCrossDeviceSignInApplication<TestProfile>));
    }

    [Fact]
    public void DisablingMandatoryTotpIsRejected()
    {
        var services = new ServiceCollection();
        var builder = services.AddSkopkaHello<TestProfile>(options =>
        {
            options.PublicOrigin = new Uri(
                "https://accounts.example.test");
            options.Totp.Enabled = true;
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddCrossDeviceSignIn(options =>
                options.RequireStepUp = false));

        Assert.Contains("TOTP", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureRejectsInsecureDevelopmentCookies()
    {
        var services = new ServiceCollection();
        var builder = services.AddSkopkaHello<TestProfile>(options =>
        {
            options.SecureCookies = false;
            options.RefreshCookieName = "Skopka.Hello.Refresh";
            options.AntiforgeryCookieName = "Skopka.Hello.Antiforgery";
            options.AntiforgeryRequestCookieName =
                "Skopka.Hello.XSRF-TOKEN";
            options.PublicOrigin = new Uri(
                "https://accounts.example.test");
            options.Totp.Enabled = true;
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddCrossDeviceSignIn());

        Assert.Contains("Secure", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Skopka.Hello.CrossDevice", SameSiteMode.Strict)]
    [InlineData("__Host-Skopka.Hello.CrossDevice", SameSiteMode.Lax)]
    public void FeatureRejectsWeakenedVerifierCookie(
        string cookieName,
        SameSiteMode sameSite)
    {
        var services = new ServiceCollection();
        var builder = services.AddSkopkaHello<TestProfile>(options =>
        {
            options.PublicOrigin = new Uri(
                "https://accounts.example.test");
            options.Totp.Enabled = true;
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddCrossDeviceSignIn(options =>
            {
                options.VerifierCookieName = cookieName;
                options.CookieSameSite = sameSite;
            }));

        Assert.Contains(
            "__Host-",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "SameSite=Strict",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VerifierCookieIsHostBoundHttpOnlySecureAndStrict()
    {
        var options = new HelloCrossDeviceSignInOptions
        {
            Enabled = true,
        };
        var manager = new HelloCrossDeviceCookieManager(options);
        var context = new DefaultHttpContext();

        manager.Write(
            context,
            "device-code",
            "browser-verifier",
            DateTimeOffset.UtcNow.AddMinutes(2));

        var cookie = Assert.Single(context.Response.Headers.SetCookie);
        Assert.StartsWith(
            "__Host-Skopka.Hello.CrossDevice=",
            cookie,
            StringComparison.Ordinal);
        Assert.Contains("; path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "; samesite=strict",
            cookie,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TestProfile(string DisplayName);
}
