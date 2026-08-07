using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.AuthorizationServer;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.IntegrationTests;

public sealed class AuthorizationServerFlowTests
{
    private const string BrowserScheme = "Test.Browser";
    private const string ClientId = "native-test";
    private const string RedirectUri = "com.example.test:/callback";
    private static readonly Uri Issuer = new("https://hello.test");

    [Fact]
    public async Task CodePkceRefreshAndLogicalRevocationAreEnforced()
    {
        await using var host = await TestAuthorizationHost.CreateAsync();
        Assert.True(host.StaleClientWasRemoved);
        const string verifier =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
        var challenge = Base64UrlEncoder.Encode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUri = QueryHelpers.AddQueryString(
            "/connect/authorize",
            new Dictionary<string, string?>
            {
                [Parameters.ClientId] = ClientId,
                [Parameters.RedirectUri] = RedirectUri,
                [Parameters.ResponseType] = ResponseTypes.Code,
                [Parameters.Scope] = "openid offline_access profile roles",
                [Parameters.CodeChallenge] = challenge,
                [Parameters.CodeChallengeMethod] = CodeChallengeMethods.Sha256,
                [Parameters.State] = "test-state",
            });

        using var authorization = await host.Client.GetAsync(authorizeUri);
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var location = Assert.IsType<Uri>(
            authorization.Headers.Location);
        Assert.Equal("com.example.test", location.Scheme);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("test-state", query[Parameters.State]);
        var code = Assert.IsType<string>(
            Assert.Single(query[Parameters.Code]));

        var tokens = await ExchangeAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = ClientId,
                [Parameters.RedirectUri] = RedirectUri,
                [Parameters.Code] = code,
                [Parameters.CodeVerifier] = verifier,
            });
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.IdentityToken));
        Assert.NotEqual(
            TestSessionRegistry.SourceSessionId,
            host.Sessions.LastRegisteredSessionId);

        var refreshed = await ExchangeAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.RefreshToken,
                [Parameters.ClientId] = ClientId,
                [Parameters.RefreshToken] = tokens.RefreshToken,
            });
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshed.RefreshToken));
        Assert.Equal(
            HttpStatusCode.OK,
            await ValidateApiAccessAsync(
                host.Client,
                refreshed.AccessToken));
        Assert.Equal(
            HttpStatusCode.OK,
            await ValidateApiAccessAsync(
                host.Client,
                refreshed.AccessToken,
                "/test/admin"));

        host.Sessions.Revoke(host.Sessions.LastRegisteredSessionId);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                refreshed.AccessToken));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                refreshed.AccessToken,
                "/test/admin"));
        using var rejectedRefresh = await PostTokenAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.RefreshToken,
                [Parameters.ClientId] = ClientId,
                [Parameters.RefreshToken] = refreshed.RefreshToken,
            });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedRefresh.StatusCode);
        using var error = JsonDocument.Parse(
            await rejectedRefresh.Content.ReadAsStringAsync());
        Assert.Equal(
            Errors.InvalidGrant,
            error.RootElement.GetProperty(Parameters.Error).GetString());
    }

    private static async Task<HttpStatusCode> ValidateApiAccessAsync(
        HttpClient client,
        string accessToken,
        string path = "/test/account")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<TokenResponse> ExchangeAsync(
        HttpClient client,
        Dictionary<string, string> form)
    {
        using var response = await PostTokenAsync(client, form);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);
        using var document = JsonDocument.Parse(payload);
        return new TokenResponse(
            document.RootElement.GetProperty(Parameters.AccessToken)
                .GetString()!,
            document.RootElement.GetProperty(Parameters.RefreshToken)
                .GetString()!,
            document.RootElement.TryGetProperty(
                Parameters.IdToken,
                out var identityToken)
                    ? identityToken.GetString()
                    : null);
    }

    private static Task<HttpResponseMessage> PostTokenAsync(
        HttpClient client,
        Dictionary<string, string> form)
        => client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(form));

    private sealed record TokenResponse(
        string AccessToken,
        string RefreshToken,
        string? IdentityToken);

    private sealed class TestAuthorizationHost : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly SqliteConnection connection;

        private TestAuthorizationHost(
            WebApplication application,
            SqliteConnection connection,
            TestSessionRegistry sessions,
            HttpClient client,
            bool staleClientWasRemoved)
        {
            this.application = application;
            this.connection = connection;
            Sessions = sessions;
            Client = client;
            StaleClientWasRemoved = staleClientWasRemoved;
        }

        public TestSessionRegistry Sessions { get; }

        public HttpClient Client { get; }

        public bool StaleClientWasRemoved { get; }

        public static async Task<TestAuthorizationHost> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sessions = new TestSessionRegistry();
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    EnvironmentName = "IntegrationTests",
                });
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton(sessions);
            builder.Services.AddSingleton<
                IIdentitySessionRegistry<TestProfile>>(sessions);
            builder.Services.AddSingleton<
                IIdentitySessionClaimsProvider<TestProfile>,
                TestClaimsProvider>();
            builder.Services.AddDbContext<TestAuthorizationDbContext>(
                options =>
                {
                    options.UseSqlite(connection);
                    options.UseOpenIddict();
                });
            builder.Services.AddOpenIddict()
                .AddCore(options => options.UseEntityFrameworkCore()
                    .UseDbContext<TestAuthorizationDbContext>());
            builder.Services.AddAuthentication()
                .AddScheme<
                    AuthenticationSchemeOptions,
                    TestBrowserAuthenticationHandler>(
                    BrowserScheme,
                    _ => { })
                .AddScheme<
                    AuthenticationSchemeOptions,
                    NoResultAuthenticationHandler>(
                    "Bearer",
                    _ => { });
            builder.Services.AddSkopkaHelloAuthorizationServer<TestProfile>(
                options =>
                {
                    options.Issuer = Issuer;
                    options.BrowserAuthenticationScheme = BrowserScheme;
                    options.Clients.Add(
                        new HelloAuthorizationClientOptions
                        {
                            ClientId = ClientId,
                            DisplayName = "Native test client",
                            Type = HelloAuthorizationClientType.Public,
                            RedirectUris = [RedirectUri],
                            Scopes =
                            [
                                Scopes.OpenId,
                                Scopes.OfflineAccess,
                                Scopes.Profile,
                                HelloAuthorizationDefaults.RolesScope,
                            ],
                        });
                },
                server =>
                {
                    server.AddEphemeralEncryptionKey();
                    server.AddEphemeralSigningKey();
                });
            builder.Services.AddAuthorizationBuilder()
                .AddPolicy(
                    "NamedTestPolicy",
                    policy => policy.RequireAuthenticatedUser());

            var application = builder.Build();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapSkopkaHelloAuthorizationServer<TestProfile>();
            application.MapGet(
                    "/test/account",
                    async (
                        HttpContext context,
                        IEnumerable<IHelloAccessTokenValidator<TestProfile>>
                            validators,
                        CancellationToken cancellationToken) =>
                    {
                        var token = context.Request.Headers.Authorization
                            .ToString()["Bearer ".Length..];
                        foreach (var validator in validators)
                        {
                            var result = await validator.ValidateAsync(
                                token,
                                cancellationToken);
                            if (result.IsSuccess)
                            {
                                return Results.Ok(new
                                {
                                    result.Value.Id,
                                });
                            }
                        }

                        return Results.Unauthorized();
                    })
                .RequireAuthorization();
            application.MapGet(
                    "/test/admin",
                    () => Results.Ok())
                .RequireAuthorization("NamedTestPolicy");

            var staleClientWasRemoved = false;
            await using (var scope =
                application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<
                    TestAuthorizationDbContext>();
                await database.Database.EnsureCreatedAsync();
                var applications = scope.ServiceProvider
                    .GetRequiredService<IOpenIddictApplicationManager>();
                await applications.CreateAsync(
                    new OpenIddictApplicationDescriptor
                    {
                        ClientId = "removed-client",
                        ClientType = ClientTypes.Public,
                        ConsentType = ConsentTypes.Implicit,
                        DisplayName = "Removed client",
                    });
                var clients = scope.ServiceProvider.GetRequiredService<
                    IHelloAuthorizationClientSynchronizer>();
                await clients.SynchronizeAsync(CancellationToken.None);
                staleClientWasRemoved = await applications
                    .FindByClientIdAsync("removed-client") is null;
            }

            await application.StartAsync();
            var client = application.GetTestClient();
            client.BaseAddress = Issuer;
            return new TestAuthorizationHost(
                application,
                connection,
                sessions,
                client,
                staleClientWasRemoved);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestAuthorizationDbContext(
        DbContextOptions<TestAuthorizationDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseOpenIddict();
        }
    }

    private sealed class TestBrowserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(Scheme.Name);
            identity.AddClaim(new Claim(
                Claims.Subject,
                TestSessionRegistry.UserId.ToString("D")));
            identity.AddClaim(new Claim(
                IdentitySessionClaimTypes.SessionId,
                TestSessionRegistry.SourceSessionId.ToString("D")));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity),
                    Scheme.Name)));
        }
    }

    private sealed class NoResultAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class TestClaimsProvider
        : IIdentitySessionClaimsProvider<TestProfile>
    {
        public Task<IReadOnlyCollection<IdentitySessionClaim>> GetClaimsAsync(
            IdentityUser<TestProfile> user,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<IdentitySessionClaim>>(
            [
                new(IdentitySessionClaimTypes.Name, user.Profile.Name),
                new(IdentitySessionClaimTypes.Role, "member"),
            ]);
    }

    private sealed class TestSessionRegistry
        : IIdentitySessionRegistry<TestProfile>
    {
        public static readonly Guid UserId = Guid.Parse(
            "a82df68e-f7c2-414a-9d75-3057b35eef1d");
        public static readonly Guid SourceSessionId = Guid.Parse(
            "a3909676-324b-441c-b231-e743a1eeab88");

        private readonly HashSet<Guid> activeSessions =
            [SourceSessionId];

        public Guid LastRegisteredSessionId { get; private set; }

        public Task<OperationResult<IdentitySessionInfo>> RegisterAsync(
            RegisterIdentitySessionCommand command,
            CancellationToken ct)
        {
            if (command.UserId != UserId)
            {
                return Task.FromResult(
                    OperationResultFactory.Fail<IdentitySessionInfo>(
                        InvalidSession()));
            }

            LastRegisteredSessionId = Guid.NewGuid();
            activeSessions.Add(LastRegisteredSessionId);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(OperationResultFactory.Success(
                new IdentitySessionInfo(
                    LastRegisteredSessionId,
                    UserId,
                    command.Metadata ?? new IdentitySessionMetadata(),
                    now.AddDays(30),
                    now,
                    now)));
        }

        public Task<OperationResult<IdentityUser<TestProfile>>> ValidateAsync(
            ValidateIdentitySessionCommand command,
            CancellationToken ct)
            => Task.FromResult(
                command.UserId == UserId
                    && activeSessions.Contains(command.SessionId)
                    ? OperationResultFactory.Success(CreateUser())
                    : OperationResultFactory.Fail<
                        IdentityUser<TestProfile>>(InvalidSession()));

        public Task<OperationResult> RevokeByIdAsync(
            RevokeIdentitySessionByIdCommand command,
            CancellationToken ct)
        {
            if (command.UserId == UserId)
            {
                activeSessions.Remove(command.SessionId);
            }

            return Task.FromResult(OperationResultFactory.Success());
        }

        public Task<OperationResult> RevokeAllAsync(
            RevokeAllIdentitySessionsCommand command,
            CancellationToken ct)
        {
            if (command.UserId == UserId)
            {
                activeSessions.Clear();
            }

            return Task.FromResult(OperationResultFactory.Success());
        }

        public Task<OperationResult<IReadOnlyList<IdentitySessionInfo>>>
            ListAsync(
                ListIdentitySessionsCommand command,
                CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success<
                IReadOnlyList<IdentitySessionInfo>>([]));

        public Task<int> PruneAsync(CancellationToken ct)
            => Task.FromResult(0);

        public void Revoke(Guid sessionId)
            => activeSessions.Remove(sessionId);

        private static IdentityUser<TestProfile> CreateUser()
        {
            var now = DateTimeOffset.UtcNow;
            return new IdentityUser<TestProfile>(
                UserId,
                UserFlags.None,
                "alice",
                "alice@example.test",
                true,
                null,
                false,
                new TestProfile("Alice"),
                1,
                "security-stamp",
                null,
                null,
                null,
                now,
                now);
        }

        private static Error InvalidSession()
            => new(
                IdentityErrorCodes.AccessTokenInvalid,
                "The session is invalid or expired.",
                ErrorType.Unauthorized);
    }

    private sealed record TestProfile(string Name);
}
