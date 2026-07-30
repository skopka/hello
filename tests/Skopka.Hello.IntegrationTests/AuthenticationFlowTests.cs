using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.Endpoints;
using Skopka.Identity.Ef.PostgreSql;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed class AuthenticationFlowTests
{
    private const string RefreshCookieName =
        "__Host-Skopka.Hello.Refresh";
    private const string AntiforgeryCookieName =
        "__Host-Skopka.Hello.Antiforgery";
    private const string AntiforgeryRequestCookieName =
        "__Host-Skopka.Hello.XSRF-TOKEN";
    private const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    [Fact]
    public async Task CompleteAuthenticationAndSessionFlow()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient();

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "alice",
                email = "alice@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Alice",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var firstLogin = await LoginAsync(client);
        Assert.Contains(
            "HttpOnly",
            firstLogin.RefreshSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Secure",
            firstLogin.RefreshSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "SameSite=Strict",
            firstLogin.RefreshSetCookie,
            StringComparison.OrdinalIgnoreCase);

        using var meRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/me");
        meRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                firstLogin.AccessToken);
        using var me = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var sessionsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/sessions");
        sessionsRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                firstLogin.AccessToken);
        using var sessions = await client.SendAsync(sessionsRequest);
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);

        var refreshed = await RefreshAsync(
            client,
            firstLogin.Cookies);
        Assert.NotEqual(
            firstLogin.AccessToken,
            refreshed.AccessToken);

        using var revokeRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/account/sessions/{refreshed.SessionId}");
        revokeRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                refreshed.AccessToken);
        using var revoked = await client.SendAsync(revokeRequest);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var secondLogin = await LoginAsync(client);
        using var logout = CreateCookieMutation(
            HttpMethod.Post,
            "/auth/logout",
            secondLogin.Cookies);
        using var loggedOut = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);

        var thirdLogin = await LoginAsync(client);
        using var logoutAll = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/logout-all");
        logoutAll.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                thirdLogin.AccessToken);
        using var allLoggedOut = await client.SendAsync(logoutAll);
        Assert.Equal(
            HttpStatusCode.NoContent,
            allLoggedOut.StatusCode);
    }

    private static async Task<LoginResult> LoginAsync(
        HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                handle = "email",
                login = "alice@example.test",
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<
            SessionPayload>();
        Assert.NotNull(payload);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.False(
            document.RootElement.TryGetProperty(
                "refreshToken",
                out _));

        var cookies = CookieSet.FromResponse(response);
        var refreshHeader = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                $"{RefreshCookieName}=",
                StringComparison.Ordinal));

        return new LoginResult(
            payload.AccessToken,
            payload.SessionId,
            cookies,
            refreshHeader);
    }

    private static async Task<LoginResult> RefreshAsync(
        HttpClient client,
        CookieSet cookies)
    {
        using var request = CreateCookieMutation(
            HttpMethod.Post,
            "/auth/refresh",
            cookies);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<
            SessionPayload>();
        Assert.NotNull(payload);
        return new LoginResult(
            payload.AccessToken,
            payload.SessionId,
            CookieSet.FromResponse(response, cookies),
            response.Headers
                .GetValues("Set-Cookie")
                .Single(value => value.StartsWith(
                    $"{RefreshCookieName}=",
                    StringComparison.Ordinal)));
    }

    private static HttpRequestMessage CreateCookieMutation(
        HttpMethod method,
        string path,
        CookieSet cookies)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookies.ToRequestHeader());
        request.Headers.TryAddWithoutValidation(
            AntiforgeryHeaderName,
            cookies.AntiforgeryRequestToken);
        return request;
    }

    private sealed record IntegrationProfile(
        string DisplayName,
        string? Locale);

    private sealed record SessionPayload(
        Guid SessionId,
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt);

    private sealed record LoginResult(
        string AccessToken,
        Guid SessionId,
        CookieSet Cookies,
        string RefreshSetCookie);

    private sealed record CookieSet(
        string RefreshToken,
        string AntiforgeryCookie,
        string AntiforgeryRequestToken)
    {
        public static CookieSet FromResponse(
            HttpResponseMessage response,
            CookieSet? fallback = null)
        {
            var values = response.Headers
                .GetValues("Set-Cookie")
                .ToArray();
            return new CookieSet(
                Read(
                    values,
                    RefreshCookieName,
                    fallback?.RefreshToken),
                Read(
                    values,
                    AntiforgeryCookieName,
                    fallback?.AntiforgeryCookie),
                Read(
                    values,
                    AntiforgeryRequestCookieName,
                    fallback?.AntiforgeryRequestToken));
        }

        public string ToRequestHeader()
            => $"{RefreshCookieName}={RefreshToken}; "
                + $"{AntiforgeryCookieName}={AntiforgeryCookie}; "
                + $"{AntiforgeryRequestCookieName}={AntiforgeryRequestToken}";

        private static string Read(
            IEnumerable<string> values,
            string name,
            string? fallback)
        {
            var prefix = $"{name}=";
            var header = values.SingleOrDefault(value =>
                value.StartsWith(
                    prefix,
                    StringComparison.Ordinal));
            if (header is null)
            {
                return fallback
                    ?? throw new InvalidOperationException(
                        $"Cookie '{name}' was not issued.");
            }

            var separator = header.IndexOf(
                ';',
                prefix.Length);
            return separator < 0
                ? header[prefix.Length..]
                : header[prefix.Length..separator];
        }
    }

    private sealed class TestApplication : IAsyncDisposable
    {
        private readonly WebApplication application;

        private TestApplication(WebApplication application)
        {
            this.application = application;
        }

        public static async Task<TestApplication> CreateAsync(
            string connectionString)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    EnvironmentName = "IntegrationTests",
                });
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(
                    IPAddress.Loopback,
                    0));

            var identity = builder.Services
                .AddSkopkaHello<IntegrationProfile>()
                .ConfigurePasswordPolicy(options =>
                {
                    options.MinimumLength = 15;
                    options.MaximumLength = 128;
                })
                .UsePostgreSql(connectionString)
                .UsePbkdf2PasswordHasher(options =>
                {
                    options.Iterations = 1_000;
                    options.MaximumAcceptedIterations = 1_000;
                })
                .UseJwtSessions(
                    RandomNumberGenerator.GetBytes(32),
                    options =>
                    {
                        options.Issuer =
                            "https://integration.skopka.test";
                        options.Audience =
                            "skopka-hello-integration";
                    });
            identity.UseJwtBearerAuthentication();
            builder.Services.AddProblemDetails();

            var application = builder.Build();
            application.UseExceptionHandler();
            application.UseStatusCodePages();
            application.Use(
                static (context, next) =>
                {
                    context.Request.Scheme = "https";
                    return next(context);
                });
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapSkopkaHello<IntegrationProfile>();

            await using (var scope =
                application.Services.CreateAsyncScope())
            {
                var database =
                    scope.ServiceProvider.GetRequiredService<
                        PostgreSqlIdentityDbContext<
                            IntegrationProfile>>();
                await database.Database.MigrateAsync();
            }

            await application.StartAsync();
            return new TestApplication(application);
        }

        public HttpClient CreateClient()
        {
            var server = application.Services
                .GetRequiredService<IServer>();
            var address = server.Features
                .Get<IServerAddressesFeature>()
                ?.Addresses
                .Single()
                ?? throw new InvalidOperationException(
                    "Kestrel did not expose its address.");
            var handler = new HttpClientHandler
            {
                UseProxy = false,
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(address),
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public async ValueTask DisposeAsync()
        {
            await application.DisposeAsync();
        }
    }
}
