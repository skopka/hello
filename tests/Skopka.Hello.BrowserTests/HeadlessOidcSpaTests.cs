using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Skopka.Hello.BrowserTests;

public sealed class HeadlessOidcSpaTests(BrowserFixture browser)
    : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task ExternalRegistrationCompletesInBrowserWithoutTokenStorage()
    {
        await using var host = await BrowserContractHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync();
        var page = await OpenSpaAsync(context, host.Origin);
        await Assertions.Expect(
                page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Sign in with Integration authority" }))
            .ToBeVisibleAsync();

        await page.RunAndWaitForPopupAsync(
            () => page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Sign in with Integration authority" })
                .ClickAsync());

        await Assertions.Expect(page.Locator("#registration-panel"))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Email"))
            .ToHaveValueAsync("external@example.test");
        await Assertions.Expect(page.GetByLabel("Display name"))
            .ToHaveValueAsync("Provider Alice");

        await page.GetByLabel("User name").FillAsync("external-alice");
        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "Create account" })
            .ClickAsync();

        await Assertions.Expect(page.Locator("#account-panel"))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#status"))
            .ToContainTextAsync("External account created.");
        var registration = Assert.IsType<ExternalRegistrationCapture>(
            host.State.Registration);
        Assert.Equal("external-alice", registration.UserName);
        Assert.Equal("external@example.test", registration.Email);
        Assert.Equal("Provider Alice", registration.Profile.DisplayName);
        Assert.Equal("en", registration.Profile.Locale);
        Assert.Empty(host.State.RequestViolations);

        Assert.Equal(
            0,
            await page.EvaluateAsync<int>("() => localStorage.length"));
        Assert.Equal(
            0,
            await page.EvaluateAsync<int>("() => sessionStorage.length"));
        Assert.DoesNotContain(
            BrowserContractState.ExternalAccessToken,
            await page.Locator("body").InnerTextAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordAccountLinksAndUnlinksProviderWithOtp()
    {
        await using var host = await BrowserContractHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync();
        var page = await OpenSpaAsync(context, host.Origin);
        await page.Locator("summary")
            .GetByText("Use a password account", new() { Exact = true })
            .ClickAsync();
        await page.GetByLabel("Login").FillAsync("password-alice");
        await page.GetByLabel("Password").FillAsync("Strong-Password-42!");
        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in with password" })
            .ClickAsync();

        await Assertions.Expect(
                page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Link Integration authority" }))
            .ToBeVisibleAsync();
        await page.RunAndWaitForPopupAsync(
            () => page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Link Integration authority" })
                .ClickAsync());

        await Assertions.Expect(page.Locator("#verification-title"))
            .ToHaveTextAsync("Verify external login linking");
        await page.GetByLabel("Verification code").FillAsync("123456");
        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "Confirm action" })
            .ClickAsync();

        await Assertions.Expect(
                page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Unlink Integration authority" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#status"))
            .ToContainTextAsync("External login linked.");

        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "Unlink Integration authority" })
            .ClickAsync();
        await Assertions.Expect(page.Locator("#verification-title"))
            .ToHaveTextAsync("Verify external login unlinking");
        await page.GetByLabel("Verification code").FillAsync("654321");
        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "Confirm action" })
            .ClickAsync();

        await Assertions.Expect(
                page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Link Integration authority" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#status"))
            .ToContainTextAsync("External login unlinked.");
        Assert.Equal(["123456", "654321"], host.State.VerificationCodes);
        Assert.Empty(host.State.RequestViolations);
        Assert.Equal(
            0,
            await page.EvaluateAsync<int>("() => localStorage.length"));
        Assert.Equal(
            0,
            await page.EvaluateAsync<int>("() => sessionStorage.length"));
    }

    private static async Task<IPage> OpenSpaAsync(
        IBrowserContext context,
        Uri origin)
    {
        var browserErrors = new ConcurrentQueue<string>();
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => browserErrors.Enqueue(error);
        page.Console += (_, message) =>
        {
            if (string.Equals(
                    message.Type,
                    "error",
                    StringComparison.Ordinal))
            {
                browserErrors.Enqueue(message.Text);
            }
        };
        page.RequestFailed += (_, request) => browserErrors.Enqueue(
            $"{request.Url}: {request.Failure}");

        await page.GotoAsync(new Uri(origin, "/app/").AbsoluteUri);
        await page.WaitForTimeoutAsync(250);
        Assert.True(
            browserErrors.IsEmpty,
            string.Join(Environment.NewLine, browserErrors));
        return page;
    }
}

public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        playwright?.Dispose();
    }
}

internal sealed class BrowserContractHost : IAsyncDisposable
{
    private readonly WebApplication application;

    private BrowserContractHost(
        WebApplication application,
        Uri origin,
        BrowserContractState state)
    {
        this.application = application;
        Origin = origin;
        State = state;
    }

    public Uri Origin { get; }

    public BrowserContractState State { get; }

    public static async Task<BrowserContractHost> CreateAsync()
    {
        var staticRoot = Path.Combine(
            AppContext.BaseDirectory,
            "SampleSpa");
        var indexPath = Path.Combine(
            staticRoot,
            "app",
            "index.html");
        if (!File.Exists(indexPath))
        {
            throw new InvalidOperationException(
                $"The sample SPA was not copied to '{indexPath}'.");
        }

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(BrowserContractHost)
                    .Assembly.FullName,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "BrowserTests",
            });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var state = new BrowserContractState();
        var application = builder.Build();
        MapContractEndpoints(
            application,
            state,
            staticRoot,
            indexPath);

        await application.StartAsync();
        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;
        var address = addresses?.SingleOrDefault()
            ?? throw new InvalidOperationException(
                "Kestrel did not publish a browser-test address.");
        return new BrowserContractHost(
            application,
            new Uri(address, UriKind.Absolute),
            state);
    }

    public async ValueTask DisposeAsync()
        => await application.DisposeAsync();

    private static void MapContractEndpoints(
        WebApplication application,
        BrowserContractState state,
        string staticRoot,
        string indexPath)
    {
        application.MapGet(
            "/app/app.js",
            () => Results.File(
                Path.Combine(staticRoot, "app", "app.js"),
                "text/javascript"));
        application.MapGet(
            "/app/app.css",
            () => Results.File(
                Path.Combine(staticRoot, "app", "app.css"),
                "text/css"));
        application.MapGet(
            "/app/config",
            () => Results.Ok(new
            {
                antiforgeryCookieName = BrowserContractState.CsrfCookie,
                antiforgeryHeaderName = BrowserContractState.CsrfHeader,
                oidcReturnPath = BrowserContractState.ReturnPath,
            }));
        application.MapGet(
            "/auth/external/providers",
            () => Results.Ok(new[]
            {
                new
                {
                    providerId = "integration",
                    displayName = "Integration authority",
                },
            }));
        application.MapGet(
            "/auth/external/{providerId}/challenge",
            (string providerId, string returnUrl, HttpContext context) =>
            {
                state.RequireProvider(providerId);
                state.BeginSignIn(returnUrl);
                state.IssueCsrf(context);
                return Results.Redirect(returnUrl);
            });
        application.MapGet(
            "/auth/external/{providerId}/link-challenge",
            (string providerId, HttpContext context) =>
            {
                state.RequireProvider(providerId);
                state.Validate(context, authenticated: false, csrf: false);
                state.BeginLinkChallenge();
                return Results.Redirect(state.ReturnUrl);
            });
        application.MapPost(
            "/auth/external/complete",
            (HttpContext context) =>
            {
                var linking = state.Flow == BrowserFlow.Link;
                state.Validate(context, authenticated: linking, csrf: true);
                if (linking)
                {
                    return Results.Ok(new
                    {
                        outcome = "LinkVerificationRequired",
                        session = (object?)null,
                        registration = (object?)null,
                        provider = new
                        {
                            providerId = "integration",
                            displayName = "Integration authority",
                        },
                        returnUrl = state.ReturnUrl,
                    });
                }

                return Results.Ok(new
                {
                    outcome = "RegistrationRequired",
                    session = (object?)null,
                    registration = new
                    {
                        provider = new
                        {
                            providerId = "integration",
                            displayName = "Integration authority",
                        },
                        displayName = "Provider Alice",
                        verifiedEmail = "external@example.test",
                        locale = "en",
                    },
                    provider = (object?)null,
                    returnUrl = state.ReturnUrl,
                });
            });
        application.MapPost(
            "/auth/external/registration",
            async (HttpContext context) =>
            {
                state.Validate(context, authenticated: false, csrf: true);
                state.Registration = await context.Request
                    .ReadFromJsonAsync<ExternalRegistrationCapture>();
                state.Linked = true;
                return Results.Ok(new
                {
                    outcome = "SignedIn",
                    session = state.CreateSession(
                        BrowserContractState.ExternalAccessToken),
                    registration = (object?)null,
                    provider = (object?)null,
                    returnUrl = state.ReturnUrl,
                });
            });
        application.MapDelete(
            "/auth/external/flow",
            (HttpContext context) =>
            {
                state.Validate(context, authenticated: false, csrf: true);
                state.Flow = BrowserFlow.None;
                return Results.NoContent();
            });
        application.MapPost(
            "/auth/login",
            (HttpContext context) =>
            {
                state.IssueCsrf(context);
                return Results.Ok(state.CreateSession(
                    BrowserContractState.PasswordAccessToken));
            });
        application.MapGet(
            "/auth/antiforgery",
            (HttpContext context) =>
            {
                state.Validate(context, authenticated: true, csrf: false);
                state.IssueCsrf(context);
                return Results.NoContent();
            });
        application.MapGet(
            "/account/external-logins",
            (HttpContext context) =>
            {
                state.Validate(context, authenticated: true, csrf: false);
                return state.Linked
                    ? Results.Ok(new[]
                    {
                        new
                        {
                            providerId = "integration",
                            displayName = "Integration authority",
                            enabled = true,
                            canUnlink = state.CurrentAccessToken
                                != BrowserContractState.ExternalAccessToken,
                            linkedAt = DateTimeOffset.UtcNow,
                        },
                    })
                    : Results.Ok(Array.Empty<object>());
            });
        application.MapPost(
            "/account/external-logins/{providerId}/link",
            async (string providerId, HttpContext context) =>
            {
                state.RequireProvider(providerId);
                state.Validate(context, authenticated: true, csrf: true);
                var request = await context.Request
                    .ReadFromJsonAsync<ExternalLinkCapture>();
                state.PrepareLink(
                    request?.ReturnUrl
                        ?? throw new InvalidOperationException(
                            "The link return URL is required."));
                return Results.Ok(new
                {
                    challengeUrl =
                        "/auth/external/integration/link-challenge",
                });
            });
        application.MapPost(
            "/account/external-logins/link/challenge",
            (HttpContext context) =>
            {
                state.Validate(context, authenticated: true, csrf: true);
                return Results.Ok(new
                {
                    challengeId = Guid.NewGuid(),
                    expiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                });
            });
        application.MapPut(
            "/account/external-logins/link",
            async (HttpContext context) =>
            {
                state.Validate(context, authenticated: true, csrf: true);
                var request = await context.Request
                    .ReadFromJsonAsync<VerificationCapture>();
                state.VerificationCodes.Enqueue(
                    request?.VerificationCode ?? string.Empty);
                state.Linked = true;
                return Results.Ok(state.CreateSession(
                    BrowserContractState.LinkedAccessToken));
            });
        application.MapPost(
            "/account/external-logins/{providerId}/unlink/challenge",
            (string providerId, HttpContext context) =>
            {
                state.RequireProvider(providerId);
                state.Validate(context, authenticated: true, csrf: true);
                return Results.Ok(new
                {
                    challengeId = Guid.NewGuid(),
                    expiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                });
            });
        application.MapDelete(
            "/account/external-logins/unlink",
            async (HttpContext context) =>
            {
                state.Validate(context, authenticated: true, csrf: true);
                var request = await context.Request
                    .ReadFromJsonAsync<VerificationCapture>();
                state.VerificationCodes.Enqueue(
                    request?.VerificationCode ?? string.Empty);
                state.Linked = false;
                return Results.Ok(state.CreateSession(
                    BrowserContractState.UnlinkedAccessToken));
            });
        application.MapGet(
            "/app",
            () => Results.File(indexPath, "text/html"));
        application.MapGet(
            "/app/{**path}",
            () => Results.File(indexPath, "text/html"));
    }
}

internal sealed class BrowserContractState
{
    public const string CsrfCookie = "Skopka.Hello.XSRF-TOKEN";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string ReturnPath = "/app/oidc-return";
    public const string ExternalAccessToken = "external-access-token";
    public const string PasswordAccessToken = "password-access-token";
    public const string LinkedAccessToken = "linked-access-token";
    public const string UnlinkedAccessToken = "unlinked-access-token";

    private int csrfVersion;

    public string CurrentAccessToken { get; private set; } = string.Empty;

    public string CurrentCsrfToken { get; private set; } = string.Empty;

    public BrowserFlow Flow { get; set; }

    public bool Linked { get; set; }

    public ExternalRegistrationCapture? Registration { get; set; }

    public string ReturnUrl { get; private set; } = ReturnPath;

    public ConcurrentQueue<string> RequestViolations { get; } = new();

    public ConcurrentQueue<string> VerificationCodes { get; } = new();

    public void BeginSignIn(string returnUrl)
    {
        RequireReturnUrl(returnUrl);
        ReturnUrl = returnUrl;
        Flow = BrowserFlow.SignIn;
    }

    public void PrepareLink(string returnUrl)
    {
        RequireReturnUrl(returnUrl);
        ReturnUrl = returnUrl;
        Flow = BrowserFlow.LinkPrepared;
    }

    public void BeginLinkChallenge()
    {
        if (Flow != BrowserFlow.LinkPrepared)
        {
            RequestViolations.Enqueue(
                "The link challenge started without its preflight.");
        }
        Flow = BrowserFlow.Link;
    }

    public void RequireProvider(string providerId)
    {
        if (!string.Equals(
                providerId,
                "integration",
                StringComparison.Ordinal))
        {
            RequestViolations.Enqueue(
                $"Unexpected provider '{providerId}'.");
        }
    }

    public void IssueCsrf(HttpContext context)
    {
        CurrentCsrfToken = $"csrf-{Interlocked.Increment(ref csrfVersion)}";
        context.Response.Cookies.Append(
            CsrfCookie,
            CurrentCsrfToken,
            new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                Path = "/",
                SameSite = SameSiteMode.Strict,
                Secure = false,
            });
    }

    public void Validate(
        HttpContext context,
        bool authenticated,
        bool csrf)
    {
        if (authenticated)
        {
            var expected = $"Bearer {CurrentAccessToken}";
            if (!string.Equals(
                    context.Request.Headers.Authorization,
                    expected,
                    StringComparison.Ordinal))
            {
                RequestViolations.Enqueue(
                    "The request did not carry the current Bearer token.");
            }
        }

        if (csrf
            && !string.Equals(
                context.Request.Headers[CsrfHeader],
                CurrentCsrfToken,
                StringComparison.Ordinal))
        {
            RequestViolations.Enqueue(
                "The request did not carry the current antiforgery token.");
        }
    }

    public object CreateSession(string accessToken)
    {
        CurrentAccessToken = accessToken;
        return new
        {
            sessionId = Guid.NewGuid(),
            accessToken,
            accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
        };
    }

    private static void RequireReturnUrl(string returnUrl)
    {
        if (!returnUrl.StartsWith(
                $"{ReturnPath}?channel=",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The SPA must use a channel-bound local OIDC return URL.");
        }
    }
}

internal enum BrowserFlow
{
    None = 0,
    SignIn = 1,
    LinkPrepared = 2,
    Link = 3,
}

internal sealed record ExternalRegistrationCapture(
    string UserName,
    string? Email,
    string? Phone,
    SampleProfileCapture Profile);

internal sealed record SampleProfileCapture(
    string DisplayName,
    string? Locale);

internal sealed record ExternalLinkCapture(string ReturnUrl);

internal sealed record VerificationCapture(string VerificationCode);
