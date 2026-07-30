using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Endpoints;
using Skopka.Hello.UI;
using Skopka.Identity.Ef.PostgreSql;
using Skopka.Identity.Errors;
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
    private const string UiCookieName = "__Host-Skopka.Hello.UI";

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

    [Fact]
    public async Task CompleteRazorUiFlow()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient(
            allowAutoRedirect: false);
        Dictionary<string, string> cookies =
            new(StringComparer.Ordinal);

        using var registerPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/register",
            cookies);
        Assert.Equal(HttpStatusCode.OK, registerPage.StatusCode);
        MergeCookies(cookies, registerPage);
        var registerHtml =
            await registerPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "/_content/Skopka.Hello.UI/css/hello.css",
            registerHtml,
            StringComparison.Ordinal);
        var registerToken = ReadInputValue(
            registerHtml,
            "__RequestVerificationToken");

        using var register = await SendFormAsync(
            client,
            "/hello/register",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.DisplayName"] = "Browser Alice",
                ["Input.Email"] = "browser-alice@example.test",
                ["Input.UserName"] = "browser-alice",
                ["Input.Phone"] = string.Empty,
                ["Input.Locale"] = "en",
                ["Input.Password"] =
                    "correct horse battery staple",
                ["Input.ConfirmPassword"] =
                    "correct horse battery staple",
                ["__RequestVerificationToken"] = registerToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.StartsWith(
            "/hello/login",
            register.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        MergeCookies(cookies, register);

        using var loginPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/login",
            cookies);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        MergeCookies(cookies, loginPage);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var loginToken = ReadInputValue(
            loginHtml,
            "__RequestVerificationToken");

        using var login = await SendFormAsync(
            client,
            "/hello/login",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.Handle"] = "email",
                ["Input.Login"] =
                    "browser-alice@example.test",
                ["Input.Password"] =
                    "correct horse battery staple",
                ["__RequestVerificationToken"] = loginToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(
            "/hello/account",
            login.Headers.Location?.OriginalString);
        MergeCookies(cookies, login);
        Assert.True(cookies.ContainsKey(UiCookieName));
        Assert.True(cookies.ContainsKey(RefreshCookieName));

        using var account = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/account",
            cookies);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        var accountHtml = await account.Content.ReadAsStringAsync();
        Assert.Contains(
            "Browser Alice",
            accountHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, account);

        using var sessionsPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/account/sessions",
            cookies);
        Assert.Equal(HttpStatusCode.OK, sessionsPage.StatusCode);
        MergeCookies(cookies, sessionsPage);
        var sessionsHtml =
            await sessionsPage.Content.ReadAsStringAsync();
        var sessionId = ReadInputValue(
            sessionsHtml,
            "sessionId");
        var revokeToken = ReadInputValue(
            sessionsHtml,
            "__RequestVerificationToken");

        using var revoke = await SendFormAsync(
            client,
            "/hello/account/sessions?handler=Revoke",
            cookies,
            new Dictionary<string, string>
            {
                ["sessionId"] = sessionId,
                ["__RequestVerificationToken"] = revokeToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);
        Assert.Equal(
            "/hello/login",
            revoke.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CompleteEmailConfirmationAndPasswordResetFlow()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient(
            allowAutoRedirect: false);

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "recovery-alice",
                email = "recovery-alice@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Recovery Alice",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var invalidRequest = await client.PostAsJsonAsync(
            "/auth/email-confirmation/request",
            new { email = "invalid" });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            invalidRequest.StatusCode);

        using var unknownConfirmation =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "unknown@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            unknownConfirmation.StatusCode);
        Assert.Empty(app.Messages);

        using var confirmationRequest =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "recovery-alice@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage = Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.EmailConfirmation);

        var loginBeforeConfirmation = await LoginAsync(
            client,
            "recovery-alice@example.test",
            "correct horse battery staple");
        using var beforeConfirmation = await GetMeAsync(
            client,
            loginBeforeConfirmation.AccessToken);
        Assert.False(ReadEmailConfirmed(beforeConfirmation));

        using var confirmationPage = await client.GetAsync(
            confirmationMessage.ActionUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirmationPage.StatusCode);
        Assert.Contains(
            "no-store",
            confirmationPage.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "no-referrer",
            confirmationPage.Headers
                .GetValues("Referrer-Policy")
                .Single());

        using var stillUnconfirmed = await GetMeAsync(
            client,
            loginBeforeConfirmation.AccessToken);
        Assert.False(ReadEmailConfirmed(stillUnconfirmed));

        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationMessage.ActionUrl.Query);
        using var confirmed = await client.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        using var afterConfirmation = await GetMeAsync(
            client,
            loginBeforeConfirmation.AccessToken);
        Assert.True(ReadEmailConfirmed(afterConfirmation));

        using var repeatedConfirmation =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "recovery-alice@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            repeatedConfirmation.StatusCode);
        Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.EmailConfirmation);

        using var unknownReset = await client.PostAsJsonAsync(
            "/auth/password-reset/request",
            new { email = "unknown@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, unknownReset.StatusCode);
        Assert.DoesNotContain(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.PasswordReset);

        using var resetRequest = await client.PostAsJsonAsync(
            "/auth/password-reset/request",
            new { email = "recovery-alice@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, resetRequest.StatusCode);
        var resetMessage = Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.PasswordReset);

        using var resetPage = await client.GetAsync(
            resetMessage.ActionUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, resetPage.StatusCode);

        var loginAfterGet = await LoginAsync(
            client,
            "recovery-alice@example.test",
            "correct horse battery staple");

        var resetQuery = QueryHelpers.ParseQuery(
            resetMessage.ActionUrl.Query);
        using var reset = await client.PostAsJsonAsync(
            "/auth/password-reset/confirm",
            new
            {
                userId = Guid.Parse(
                    resetQuery["userId"].Single()!),
                token = resetQuery["token"].Single(),
                newPassword = "new correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var oldPassword = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                handle = "email",
                login = "recovery-alice@example.test",
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        var newPassword = await LoginAsync(
            client,
            "recovery-alice@example.test",
            "new correct horse battery staple");
        Assert.NotEqual(
            loginAfterGet.AccessToken,
            newPassword.AccessToken);
    }

    [Fact]
    public async Task PasswordFailuresUseVersionedPersistentRateLimiting()
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
                userName = "limited-alice",
                email = "limited-alice@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Limited Alice",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await client.PostAsJsonAsync(
                "/auth/login",
                new
                {
                    handle = "email",
                    login = "limited-alice@example.test",
                    password =
                        "incorrect horse battery staple",
                });
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                rejected.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                handle = "email",
                login = "limited-alice@example.test",
                password = "correct horse battery staple",
            });

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limited.StatusCode);
        using var problem = JsonDocument.Parse(
            await limited.Content.ReadAsStringAsync());
        Assert.Equal(
            IdentityErrorCodes.RateLimitExceeded,
            problem.RootElement.GetProperty("code").GetString());
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(
            ["v1", "v2"],
            await app.GetRateLimitVersionsAsync(
                "password.account"));
    }

    private static async Task<HttpResponseMessage> SendFormAsync(
        HttpClient client,
        string path,
        IReadOnlyDictionary<string, string> cookies,
        IReadOnlyDictionary<string, string> form)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = new FormUrlEncodedContent(form),
        };
        AddCookies(request, cookies);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string> cookies)
    {
        var request = new HttpRequestMessage(method, path);
        AddCookies(request, cookies);
        return await client.SendAsync(request);
    }

    private static void AddCookies(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> cookies)
    {
        if (cookies.Count == 0)
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Join(
                "; ",
                cookies
                    .Where(pair => pair.Value.Length > 0)
                    .Select(pair => $"{pair.Key}={pair.Value}")));
    }

    private static void MergeCookies(
        Dictionary<string, string> cookies,
        HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(
                "Set-Cookie",
                out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            var pair = value.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            cookies[pair[..separator]] =
                pair[(separator + 1)..];
        }
    }

    private static string ReadInputValue(
        string html,
        string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(
            match.Success,
            $"Input '{name}' was not found in the rendered page.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<LoginResult> LoginAsync(
        HttpClient client,
        string login = "alice@example.test",
        string password = "correct horse battery staple")
    {
        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                handle = "email",
                login,
                password,
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

    private static async Task<JsonDocument> GetMeAsync(
        HttpClient client,
        string accessToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
    }

    private static bool ReadEmailConfirmed(JsonDocument account)
        => account.RootElement
            .GetProperty("emailConfirmed")
            .GetBoolean();

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

    private sealed class IntegrationProfileUiFactory
        : IHelloUiProfileFactory<IntegrationProfile>
    {
        public OperationResult<IntegrationProfile> Create(
            HelloUiRegistrationProfile profile)
            => OperationResultFactory.Success(
                new IntegrationProfile(
                    profile.DisplayName,
                    profile.Locale));

        public string GetDisplayName(
            IntegrationProfile profile)
            => profile.DisplayName;
    }

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

    private sealed class RecordingAccountMessageSender
        : IHelloAccountMessageSender
    {
        private readonly object sync = new();
        private readonly List<HelloAccountMessage> messages = [];

        public IReadOnlyList<HelloAccountMessage> Messages
        {
            get
            {
                lock (sync)
                {
                    return messages.ToArray();
                }
            }
        }

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                messages.Add(message);
            }

            return Task.FromResult(
                OperationResultFactory.Success());
        }
    }

    private sealed class TestApplication : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly RecordingAccountMessageSender messageSender;

        private TestApplication(
            WebApplication application,
            RecordingAccountMessageSender messageSender)
        {
            this.application = application;
            this.messageSender = messageSender;
        }

        public IReadOnlyList<HelloAccountMessage> Messages =>
            messageSender.Messages;

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
            var messageSender =
                new RecordingAccountMessageSender();
            builder.Services.AddSingleton<
                IHelloAccountMessageSender>(messageSender);

            var identity = builder.Services
                .AddSkopkaHello<IntegrationProfile>(options =>
                {
                    options.PublicOrigin = new Uri(
                        "https://integration.skopka.test");
                })
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
                .UseDataProtectionActionTokens()
                .UseJwtSessions(
                    RandomNumberGenerator.GetBytes(32),
                    options =>
                    {
                        options.Issuer =
                            "https://integration.skopka.test";
                        options.Audience =
                            "skopka-hello-integration";
                    });
            var rateLimitKeys = new Dictionary<string, byte[]>
            {
                ["v1"] = RandomNumberGenerator.GetBytes(32),
                ["v2"] = RandomNumberGenerator.GetBytes(32),
            };
            try
            {
                identity.UseHmacRateLimiting(
                    "v2",
                    rateLimitKeys);
            }
            finally
            {
                foreach (var key in rateLimitKeys.Values)
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }

            identity.UseJwtBearerAuthentication();
            builder.Services.AddProblemDetails();
            builder.Services.AddSkopkaHelloUi<
                IntegrationProfile,
                IntegrationProfileUiFactory>();

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
            application.MapSkopkaHelloUi();

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
            return new TestApplication(
                application,
                messageSender);
        }

        public HttpClient CreateClient(
            bool allowAutoRedirect = true)
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
                UseCookies = false,
                AllowAutoRedirect = allowAutoRedirect,
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(address),
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public async Task<string[]> GetRateLimitVersionsAsync(
            string scope)
        {
            await using var serviceScope =
                application.Services.CreateAsyncScope();
            var database = serviceScope.ServiceProvider
                .GetRequiredService<
                    PostgreSqlIdentityDbContext<
                        IntegrationProfile>>();
            return await database.RateLimitBuckets
                .AsNoTracking()
                .Where(bucket => bucket.Scope == scope)
                .Select(bucket => bucket.PartitionVersion)
                .Distinct()
                .OrderBy(version => version)
                .ToArrayAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await application.DisposeAsync();
        }
    }
}
