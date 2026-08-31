using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
using Microsoft.IdentityModel.JsonWebTokens;
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
    private const string PortalClientId = "home-portal";
    private const string PortalClientSecret = "home-portal-test-secret";
    private const string PortalRedirectUri =
        "https://home.example.test/signin-oidc";
    private const string PortalPostLogoutRedirectUri =
        "https://home.example.test/signout-callback-oidc";
    private const string RoundcubeClientId = "roundcube";
    private const string RoundcubeClientSecret = "roundcube-test-secret";
    private const string RoundcubeRedirectUri =
        "https://webmail.example.test/index.php/login/oauth";
    private const string HelloResource = "skopka-hello-api";
    private const string MailResource = "stalwart";
    private const string MailScope = "mail";
    private static readonly Uri Issuer = new("https://hello.test");
    private static readonly byte[] IdentitySigningKey =
        SHA256.HashData(Encoding.UTF8.GetBytes(
            "Skopka.Hello integration identity signing key"));

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
        Assert.NotEqual(2, tokens.AccessToken.Count(character => character == '.'));
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
        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);
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

    [Fact]
    public async Task EndSessionReturnsToRegisteredClient()
    {
        await using var host = await TestAuthorizationHost.CreateAsync();
        const string verifier =
            "portal-abcdefghijklmnopqrstuvwxyz-ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789";
        var code = await AuthorizeCodeAsync(
            host.Client,
            PortalClientId,
            PortalRedirectUri,
            "openid offline_access profile email",
            verifier);
        var tokens = await ExchangeAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = PortalClientId,
                [Parameters.ClientSecret] = PortalClientSecret,
                [Parameters.RedirectUri] = PortalRedirectUri,
                [Parameters.Code] = code,
                [Parameters.CodeVerifier] = verifier,
            });

        var logoutUri = QueryHelpers.AddQueryString(
            "/connect/logout",
            new Dictionary<string, string?>
            {
                [Parameters.IdTokenHint] = tokens.IdentityToken,
                [Parameters.PostLogoutRedirectUri] =
                    PortalPostLogoutRedirectUri,
                [Parameters.State] = "logout-state",
            });
        using var logout = await host.Client.GetAsync(logoutUri);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var location = Assert.IsType<Uri>(logout.Headers.Location);
        Assert.Equal(
            PortalPostLogoutRedirectUri,
            location.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            "logout-state",
            QueryHelpers.ParseQuery(location.Query)[Parameters.State]);
        Assert.Equal(1, host.SessionTerminator.CallCount);
    }

    [Fact]
    public async Task RoundcubeConfidentialFlowProducesStalwartJwtAndRotatesRefresh()
    {
        await using var host = await TestAuthorizationHost.CreateAsync(
            HelloAuthorizationAccessTokenFormat.SelfContainedJwt);
        const string verifier =
            "roundcube-abcdefghijklmnopqrstuvwxyz-ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789";
        var code = await AuthorizeCodeAsync(
            host.Client,
            RoundcubeClientId,
            RoundcubeRedirectUri,
            "openid offline_access profile email mail",
            verifier);
        var tokens = await ExchangeAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = RoundcubeClientId,
                [Parameters.ClientSecret] = RoundcubeClientSecret,
                [Parameters.RedirectUri] = RoundcubeRedirectUri,
                [Parameters.Code] = code,
                [Parameters.CodeVerifier] = verifier,
            });

        Assert.Equal(2, tokens.AccessToken.Count(character => character == '.'));
        Assert.NotEqual(2, tokens.RefreshToken.Count(character => character == '.'));
        Assert.NotNull(tokens.IdentityToken);

        using var discovery = JsonDocument.Parse(
            await host.Client.GetStringAsync(
                "/.well-known/openid-configuration"));
        Assert.Equal(
            Issuer.AbsoluteUri,
            discovery.RootElement.GetProperty("issuer").GetString());
        Assert.Equal(
            Issuer + "connect/logout",
            discovery.RootElement.GetProperty("end_session_endpoint")
                .GetString());
        var jwksUri = discovery.RootElement.GetProperty("jwks_uri")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(jwksUri));
        var jwksJson = await host.Client.GetStringAsync(jwksUri);
        using (var jwksDocument = JsonDocument.Parse(jwksJson))
        {
            foreach (var key in jwksDocument.RootElement
                .GetProperty("keys")
                .EnumerateArray())
            {
                Assert.False(key.TryGetProperty("d", out _));
                Assert.False(key.TryGetProperty("p", out _));
                Assert.False(key.TryGetProperty("q", out _));
                Assert.False(key.TryGetProperty("dp", out _));
                Assert.False(key.TryGetProperty("dq", out _));
                Assert.False(key.TryGetProperty("qi", out _));
                Assert.False(key.TryGetProperty("k", out _));
            }
        }

        var handler = new JsonWebTokenHandler();
        var validation = await handler.ValidateTokenAsync(
            tokens.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer.AbsoluteUri,
                ValidateAudience = true,
                ValidAudience = MailResource,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = new JsonWebKeySet(jwksJson).Keys,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ValidTypes = ["at+jwt"],
                ClockSkew = TimeSpan.Zero,
            });
        Assert.True(validation.IsValid, validation.Exception?.ToString());

        var jwt = handler.ReadJsonWebToken(tokens.AccessToken);
        Assert.Equal(
            TestSessionRegistry.UserId.ToString("D"),
            jwt.Subject);
        Assert.Contains(MailResource, jwt.Audiences);
        Assert.NotNull(jwt.GetClaim(Claims.JwtId));
        Assert.NotNull(jwt.GetClaim(Claims.IssuedAt));
        Assert.Equal(
            host.Sessions.LastRegisteredSessionId.ToString("D"),
            jwt.GetClaim(IdentitySessionClaimTypes.SessionId).Value);
        Assert.Equal(
            "alice@example.test",
            jwt.GetClaim(IdentitySessionClaimTypes.PreferredUserName).Value);
        Assert.Equal(
            "alice@example.test",
            jwt.GetClaim(IdentitySessionClaimTypes.Email).Value);
        Assert.Equal(
            "true",
            jwt.GetClaim(IdentitySessionClaimTypes.EmailVerified).Value);
        Assert.Equal(
            "Alice",
            jwt.GetClaim(IdentitySessionClaimTypes.Name).Value);
        var scopes = jwt.GetClaim(Claims.Scope).Value.Split(' ');
        Assert.Contains(Scopes.OpenId, scopes);
        Assert.Contains(Scopes.Email, scopes);
        Assert.Contains(MailScope, scopes);

        var refreshed = await ExchangeAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.RefreshToken,
                [Parameters.ClientId] = RoundcubeClientId,
                [Parameters.ClientSecret] = RoundcubeClientSecret,
                [Parameters.RefreshToken] = tokens.RefreshToken,
            });
        Assert.Equal(2, refreshed.AccessToken.Count(character => character == '.'));
        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);

    }

    [Fact]
    public async Task RoundcubeFlowRejectsInvalidPkceSecretRedirectResourceAndScope()
    {
        await using var host = await TestAuthorizationHost.CreateAsync(
            HelloAuthorizationAccessTokenFormat.SelfContainedJwt);

        using (var missingPkce = await host.Client.GetAsync(
            CreateAuthorizeUri(
                RoundcubeClientId,
                RoundcubeRedirectUri,
                "openid email mail",
                challenge: null)))
        {
            await AssertProtocolErrorAsync(missingPkce, Errors.InvalidRequest);
        }

        const string verifier =
            "negative-abcdefghijklmnopqrstuvwxyz-ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789";
        var code = await AuthorizeCodeAsync(
            host.Client,
            RoundcubeClientId,
            RoundcubeRedirectUri,
            "openid offline_access email mail",
            verifier);
        using (var wrongVerifier = await PostTokenAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = RoundcubeClientId,
                [Parameters.ClientSecret] = RoundcubeClientSecret,
                [Parameters.RedirectUri] = RoundcubeRedirectUri,
                [Parameters.Code] = code,
                [Parameters.CodeVerifier] = verifier + "-wrong",
            }))
        {
            await AssertProtocolErrorAsync(wrongVerifier, Errors.InvalidGrant);
        }

        code = await AuthorizeCodeAsync(
            host.Client,
            RoundcubeClientId,
            RoundcubeRedirectUri,
            "openid email mail",
            verifier);
        using (var wrongSecret = await PostTokenAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = RoundcubeClientId,
                [Parameters.ClientSecret] = "wrong-secret",
                [Parameters.RedirectUri] = RoundcubeRedirectUri,
                [Parameters.Code] = code,
                [Parameters.CodeVerifier] = verifier,
            }))
        {
            await AssertProtocolErrorAsync(wrongSecret, Errors.InvalidClient);
        }

        using (var wrongRedirect = await host.Client.GetAsync(
            CreateAuthorizeUri(
                RoundcubeClientId,
                "https://attacker.example.test/callback",
                "openid email mail",
                CreateChallenge(verifier))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrongRedirect.StatusCode);
        }

        using (var foreignResource = await host.Client.GetAsync(
            CreateAuthorizeUri(
                RoundcubeClientId,
                RoundcubeRedirectUri,
                "openid email mail",
                CreateChallenge(verifier),
                HelloResource)))
        {
            await AssertProtocolErrorAsync(
                foreignResource,
                Errors.InvalidRequest);
        }

        using var forbiddenScope = await host.Client.GetAsync(
            CreateAuthorizeUri(
                RoundcubeClientId,
                RoundcubeRedirectUri,
                "openid email mail roles",
                CreateChallenge(verifier)));
        await AssertProtocolErrorAsync(forbiddenScope, Errors.InvalidRequest);
    }

    [Fact]
    public async Task CompositeBearerSeparatesIdentityOAuthJwtAndIdTokens()
    {
        await using var host = await TestAuthorizationHost.CreateAsync(
            HelloAuthorizationAccessTokenFormat.SelfContainedJwt);
        const string portalVerifier =
            "portal-abcdefghijklmnopqrstuvwxyz-ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789";
        var portalCode = await AuthorizeCodeAsync(
            host.Client,
            PortalClientId,
            PortalRedirectUri,
            "openid offline_access profile email",
            portalVerifier);
        var portal = await ExchangeAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = PortalClientId,
                [Parameters.ClientSecret] = PortalClientSecret,
                [Parameters.RedirectUri] = PortalRedirectUri,
                [Parameters.Code] = portalCode,
                [Parameters.CodeVerifier] = portalVerifier,
            });
        var portalSessionId = host.Sessions.LastRegisteredSessionId;

        Assert.Equal(
            HttpStatusCode.OK,
            await ValidateApiAccessAsync(
                host.Client,
                portal.AccessToken,
                "/test/admin"));
        Assert.Equal(
            HttpStatusCode.OK,
            await ValidateApiAccessAsync(
                host.Client,
                CreateIdentityJwt(Issuer.AbsoluteUri, HelloResource, "JWT"),
                "/test/admin"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                CreateIdentityJwt(
                    "https://wrong-issuer.example.test/",
                    HelloResource,
                    "JWT"),
                "/test/admin"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                CreateIdentityJwt(Issuer.AbsoluteUri, "wrong-audience", "JWT"),
                "/test/admin"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                CreateIdentityJwt(
                    Issuer.AbsoluteUri,
                    HelloResource,
                    "at+jwt"),
                "/test/admin"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                "eyJ0eXAiOiJhdCtqd3QifQ.e30.invalid",
                "/test/admin"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                portal.IdentityToken!,
                "/test/admin"));

        const string mailVerifier =
            "mail-abcdefghijklmnopqrstuvwxyz-ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789";
        var mailCode = await AuthorizeCodeAsync(
            host.Client,
            RoundcubeClientId,
            RoundcubeRedirectUri,
            "openid offline_access email mail",
            mailVerifier);
        var mail = await ExchangeAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = RoundcubeClientId,
                [Parameters.ClientSecret] = RoundcubeClientSecret,
                [Parameters.RedirectUri] = RoundcubeRedirectUri,
                [Parameters.Code] = mailCode,
                [Parameters.CodeVerifier] = mailVerifier,
            });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                mail.AccessToken,
                "/test/admin"));

        host.Sessions.Revoke(portalSessionId);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await ValidateApiAccessAsync(
                host.Client,
                portal.AccessToken,
                "/test/admin"));
        using var rejectedRefresh = await PostTokenAsync(
            host.Client,
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.RefreshToken,
                [Parameters.ClientId] = PortalClientId,
                [Parameters.ClientSecret] = PortalClientSecret,
                [Parameters.RefreshToken] = portal.RefreshToken,
            });
        await AssertProtocolErrorAsync(
            rejectedRefresh,
            Errors.InvalidGrant);
    }

    private static async Task<string> AuthorizeCodeAsync(
        HttpClient client,
        string clientId,
        string redirectUri,
        string scopes,
        string verifier)
    {
        using var response = await client.GetAsync(
            CreateAuthorizeUri(
                clientId,
                redirectUri,
                scopes,
                CreateChallenge(verifier)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.False(query.ContainsKey(Parameters.Error));
        return Assert.IsType<string>(
            Assert.Single(query[Parameters.Code]));
    }

    private static string CreateAuthorizeUri(
        string clientId,
        string redirectUri,
        string scopes,
        string? challenge,
        string? resource = null)
    {
        var parameters = new Dictionary<string, string?>
        {
            [Parameters.ClientId] = clientId,
            [Parameters.RedirectUri] = redirectUri,
            [Parameters.ResponseType] = ResponseTypes.Code,
            [Parameters.Scope] = scopes,
            [Parameters.State] = "integration-state",
        };
        if (challenge is not null)
        {
            parameters[Parameters.CodeChallenge] = challenge;
            parameters[Parameters.CodeChallengeMethod] =
                CodeChallengeMethods.Sha256;
        }

        if (resource is not null)
        {
            parameters[Parameters.Resource] = resource;
        }

        return QueryHelpers.AddQueryString(
            "/connect/authorize",
            parameters);
    }

    private static string CreateChallenge(string verifier)
        => Base64UrlEncoder.Encode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static async Task AssertProtocolErrorAsync(
        HttpResponseMessage response,
        string expectedError)
    {
        Assert.False(response.IsSuccessStatusCode);
        if (response.Headers.Location is { } location)
        {
            var query = QueryHelpers.ParseQuery(location.Query);
            Assert.Equal(expectedError, query[Parameters.Error]);
            return;
        }

        var payload = await response.Content.ReadAsStringAsync();
        if (payload.TrimStart().StartsWith('{'))
        {
            using var document = JsonDocument.Parse(payload);
            Assert.Equal(
                expectedError,
                document.RootElement.GetProperty(Parameters.Error).GetString());
            return;
        }

        var parameters = QueryHelpers.ParseQuery("?" + payload);
        if (!parameters.TryGetValue(Parameters.Error, out var error))
        {
            error = payload
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith(
                    Parameters.Error + ":",
                    StringComparison.Ordinal))?
                [(Parameters.Error.Length + 1)..]
                .Trim();
        }

        Assert.True(
            !string.IsNullOrWhiteSpace(error),
            $"Protocol response did not contain an error: {payload}");
        Assert.Equal(expectedError, error);
    }

    private static string CreateIdentityJwt(
        string tokenIssuer,
        string audience,
        string tokenType)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = tokenIssuer,
                Audience = audience,
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(5),
                TokenType = tokenType,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(IdentitySigningKey),
                    SecurityAlgorithms.HmacSha256),
                Subject = new ClaimsIdentity(
                [
                    new Claim(
                        Claims.Subject,
                        TestSessionRegistry.UserId.ToString("D")),
                    new Claim(
                        IdentitySessionClaimTypes.SessionId,
                        TestSessionRegistry.SourceSessionId.ToString("D")),
                ]),
            });
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
            TestAuthorizationSessionTerminator sessionTerminator,
            HttpClient client,
            bool staleClientWasRemoved)
        {
            this.application = application;
            this.connection = connection;
            Sessions = sessions;
            SessionTerminator = sessionTerminator;
            Client = client;
            StaleClientWasRemoved = staleClientWasRemoved;
        }

        public TestSessionRegistry Sessions { get; }

        public TestAuthorizationSessionTerminator SessionTerminator { get; }

        public HttpClient Client { get; }

        public bool StaleClientWasRemoved { get; }

        public static async Task<TestAuthorizationHost> CreateAsync(
            HelloAuthorizationAccessTokenFormat accessTokenFormat =
                HelloAuthorizationAccessTokenFormat.Reference)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sessions = new TestSessionRegistry();
            var sessionTerminator =
                new TestAuthorizationSessionTerminator();
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    EnvironmentName = "IntegrationTests",
                });
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton(sessions);
            builder.Services.AddSingleton<
                IHelloAuthorizationSessionTerminator>(sessionTerminator);
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
                .AddJwtBearer(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.MapInboundClaims = false;
                        options.TokenValidationParameters = new()
                        {
                            ValidateIssuer = true,
                            ValidIssuer = Issuer.AbsoluteUri,
                            ValidateAudience = true,
                            ValidAudience = HelloResource,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(
                                IdentitySigningKey),
                            ValidAlgorithms =
                            [
                                SecurityAlgorithms.HmacSha256,
                            ],
                            ValidTypes = ["JWT"],
                            RequireSignedTokens = true,
                            RequireExpirationTime = true,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero,
                        };
                    });
            builder.Services.AddSkopkaHelloAuthorizationServer<TestProfile>(
                options =>
                {
                    options.Issuer = Issuer;
                    options.BrowserAuthenticationScheme = BrowserScheme;
                    options.Resource = HelloResource;
                    options.AccessTokenFormat = accessTokenFormat;
                    options.AccessTokenLifetime = TimeSpan.FromMinutes(5);
                    options.AdditionalScopes.Add(MailScope);
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
                    options.Clients.Add(
                        new HelloAuthorizationClientOptions
                        {
                            ClientId = PortalClientId,
                            DisplayName = "Home Portal",
                            Type = HelloAuthorizationClientType.Confidential,
                            ClientSecret = PortalClientSecret,
                            Resource = HelloResource,
                            RedirectUris = [PortalRedirectUri],
                            PostLogoutRedirectUris =
                            [
                                PortalPostLogoutRedirectUri,
                            ],
                            Scopes =
                            [
                                Scopes.OpenId,
                                Scopes.OfflineAccess,
                                Scopes.Profile,
                                Scopes.Email,
                            ],
                        });
                    options.Clients.Add(
                        new HelloAuthorizationClientOptions
                        {
                            ClientId = RoundcubeClientId,
                            DisplayName = "Roundcube Webmail",
                            Type = HelloAuthorizationClientType.Confidential,
                            ClientSecret = RoundcubeClientSecret,
                            Resource = MailResource,
                            RedirectUris = [RoundcubeRedirectUri],
                            Scopes =
                            [
                                Scopes.OpenId,
                                Scopes.OfflineAccess,
                                Scopes.Profile,
                                Scopes.Email,
                                MailScope,
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
                sessionTerminator,
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

    private sealed class TestAuthorizationSessionTerminator
        : IHelloAuthorizationSessionTerminator
    {
        public int CallCount { get; private set; }

        public Task TerminateAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
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

    private sealed class TestClaimsProvider
        : IIdentitySessionClaimsProvider<TestProfile>
    {
        public Task<IReadOnlyCollection<IdentitySessionClaim>> GetClaimsAsync(
            IdentityUser<TestProfile> user,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<IdentitySessionClaim>>(
            [
                new(IdentitySessionClaimTypes.Name, user.Profile.Name),
                new(IdentitySessionClaimTypes.PreferredUserName, "alice"),
                new(
                    IdentitySessionClaimTypes.PreferredUserName,
                    "alice@example.test"),
                new(
                    IdentitySessionClaimTypes.Email,
                    "alice@example.test"),
                new(IdentitySessionClaimTypes.EmailVerified, "true"),
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
