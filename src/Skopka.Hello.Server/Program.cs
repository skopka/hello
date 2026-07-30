using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Server;
using Skopka.Hello.UI;
using Skopka.Identity.Ef.PostgreSql;

if (args is ["--health-check"])
{
    using var healthClient = new HttpClient
    {
        BaseAddress = new Uri("http://127.0.0.1:8080"),
        Timeout = TimeSpan.FromSeconds(2),
    };

    try
    {
        using var response = await healthClient.GetAsync(
            "/health/live",
            CancellationToken.None);
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        Environment.ExitCode = 1;
    }
    catch (TaskCanceledException)
    {
        Environment.ExitCode = 1;
    }

    return;
}

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var connectionString = configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required.");
var signingKey = ReadSigningKey(
    configuration["SkopkaHello:Jwt:SigningKey"]);
var issuer = configuration["SkopkaHello:Jwt:Issuer"]
    ?? "https://localhost:8080";
var audience = configuration["SkopkaHello:Jwt:Audience"]
    ?? "skopka-hello-api";
var secureCookies = configuration.GetValue(
    "SkopkaHello:Cookies:Secure",
    true);
var publicOrigin = new Uri(
    configuration["SkopkaHello:PublicOrigin"]
        ?? throw new InvalidOperationException(
            "SkopkaHello:PublicOrigin is required."),
    UriKind.Absolute);
var useForwardedHeaders = configuration.GetValue(
    "SkopkaHello:ForwardedHeaders:Enabled",
    false);
var knownProxies = configuration
    .GetSection("SkopkaHello:ForwardedHeaders:KnownProxies")
    .Get<string[]>()
    ?? [];

if (useForwardedHeaders && knownProxies.Length == 0)
{
    throw new InvalidOperationException(
        "At least one trusted forwarded-header proxy is required.");
}

if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(
        options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            foreach (var proxy in knownProxies)
            {
                if (!IPAddress.TryParse(proxy, out var address))
                {
                    throw new InvalidOperationException(
                        $"Known proxy '{proxy}' is not an IP address.");
                }

                options.KnownProxies.Add(address);
            }
        });
}

var dataProtectionKeyPath =
    configuration["SkopkaHello:DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    builder.Services
        .AddDataProtection()
        .SetApplicationName("Skopka.Hello")
        .PersistKeysToFileSystem(
            new DirectoryInfo(dataProtectionKeyPath));
}

var identity = builder.Services
    .AddSkopkaHello<HelloProfile>(options =>
    {
        options.SecureCookies = secureCookies;
        options.ClientName = "Skopka.Hello.Server";
        options.PublicOrigin = publicOrigin;
        if (!secureCookies)
        {
            options.RefreshCookieName =
                "Skopka.Hello.Refresh";
            options.AntiforgeryCookieName =
                "Skopka.Hello.Antiforgery";
            options.AntiforgeryRequestCookieName =
                "Skopka.Hello.XSRF-TOKEN";
        }
    })
    .ConfigurePasswordPolicy(options =>
    {
        options.MinimumLength = 15;
        options.MaximumLength = 128;
    })
    .UsePostgreSql(connectionString)
    .UsePbkdf2PasswordHasher()
    .UseDataProtectionActionTokens()
    .UseJwtSessions(
        signingKey,
        options =>
        {
            options.Issuer = issuer;
            options.Audience = audience;
        });

identity.UseJwtBearerAuthentication(options =>
{
    options.ValidateSessionOnEveryRequest = configuration.GetValue(
        "SkopkaHello:Jwt:ValidateSessionOnEveryRequest",
        false);
});

var smtpSection = configuration.GetSection(
    "SkopkaHello:Delivery:Smtp");
if (!string.IsNullOrWhiteSpace(smtpSection["Host"]))
{
    builder.Services.AddSkopkaHelloSmtpDelivery(
        options => smtpSection.Bind(options));
}

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSkopkaHelloUi<
    HelloProfile,
    HelloProfileUiFactory>(options =>
{
    options.CustomCssFilePath =
        configuration["SkopkaHello:Customization:CssFilePath"];
    options.CustomCssRequestPath =
        configuration["SkopkaHello:Customization:CssRequestPath"]
        ?? SkopkaHelloUiOptions.DefaultCustomCssRequestPath;
    options.BuiltInStylesEnabled = configuration.GetValue(
        "SkopkaHello:Customization:BuiltInStylesEnabled",
        true);
    options.SecureCookies = secureCookies;
    if (!secureCookies)
    {
        options.AuthenticationCookieName =
            "Skopka.Hello.UI";
    }
});
builder.Services.AddHostedService<
    IdentitySessionPruningWorker<HelloProfile>>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (configuration.GetValue(
        "SkopkaHello:Https:UseRedirection",
        false))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapGet(
    "/",
    () => TypedResults.Ok(
        new
        {
            name = "Skopka.Hello",
            version = "0.1.0",
        }));
app.MapGet(
    "/health/live",
    () => TypedResults.Ok(new { status = "healthy" }));
app.MapGet(
    "/health/ready",
    async (
        PostgreSqlIdentityDbContext<HelloProfile> database,
        CancellationToken cancellationToken) =>
        await database.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable));
app.MapSkopkaHello<HelloProfile>();
app.MapSkopkaHelloUi();

if (configuration.GetValue(
        "SkopkaHello:Database:ApplyMigrations",
        false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<
        PostgreSqlIdentityDbContext<HelloProfile>>();
    await database.Database.MigrateAsync();
}

await app.RunAsync();

static byte[] ReadSigningKey(string? encoded)
{
    if (string.IsNullOrWhiteSpace(encoded))
    {
        throw new InvalidOperationException(
            "SkopkaHello:Jwt:SigningKey is required and must be a Base64-encoded key.");
    }

    byte[] key;
    try
    {
        key = Convert.FromBase64String(encoded);
    }
    catch (FormatException exception)
    {
        throw new InvalidOperationException(
            "SkopkaHello:Jwt:SigningKey must be Base64 encoded.",
            exception);
    }

    if (key.Length < 32)
    {
        throw new InvalidOperationException(
            "SkopkaHello:Jwt:SigningKey must contain at least 32 bytes.");
    }

    return key;
}

public partial class Program;
