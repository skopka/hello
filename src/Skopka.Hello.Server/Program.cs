using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Reflection;
using Skopka.Hello;
using Skopka.Hello.Admin;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Server;
using Skopka.Hello.UI;
using Skopka.Identity.Ef.PostgreSql;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Commands;
using Skopka.Identity.Sessions;
using Skopka.Identity.Verification;

if (args is ["--health-check"])
{
    using var healthClient = new HttpClient();
    healthClient.BaseAddress = new Uri("http://127.0.0.1:8080");
    healthClient.Timeout = TimeSpan.FromSeconds(2);

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
var publicOrigin = new Uri(
    configuration["SkopkaHello:PublicOrigin"]
        ?? throw new InvalidOperationException(
            "SkopkaHello:PublicOrigin is required."),
    UriKind.Absolute);
var issuer = configuration["SkopkaHello:Jwt:Issuer"]
    ?? publicOrigin.GetLeftPart(UriPartial.Authority);
var audience = configuration["SkopkaHello:Jwt:Audience"]
    ?? "skopka-hello-api";
var secureCookies = configuration.GetValue(
    "SkopkaHello:Cookies:Secure",
    true);
var selfRegistrationEnabled = configuration.GetValue(
    "SkopkaHello:SelfRegistration:Enabled",
    true);
var registrationClientPermitLimit = configuration.GetValue(
    "SkopkaHello:SelfRegistration:ClientPermitLimit",
    5);
var registrationClientWindow = configuration.GetValue(
    "SkopkaHello:SelfRegistration:ClientWindow",
    TimeSpan.FromHours(1));
var registrationGlobalPermitLimit = configuration.GetValue(
    "SkopkaHello:SelfRegistration:GlobalPermitLimit",
    100);
var registrationGlobalWindow = configuration.GetValue(
    "SkopkaHello:SelfRegistration:GlobalWindow",
    TimeSpan.FromMinutes(1));
var uiPathPrefix = configuration[
        "SkopkaHello:Ui:PathPrefix"]
    ?? HelloUiRoutePaths.DefaultPathPrefix;
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
        options.SelfRegistrationEnabled = selfRegistrationEnabled;
        options.RegistrationClientPermitLimit =
            registrationClientPermitLimit;
        options.RegistrationClientWindow = registrationClientWindow;
        options.RegistrationGlobalPermitLimit =
            registrationGlobalPermitLimit;
        options.RegistrationGlobalWindow = registrationGlobalWindow;
        options.UiPathPrefix = uiPathPrefix;
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

using (var rateLimitKeys = VersionedSecretKeySet.Load(
    configuration.GetSection(
        "SkopkaHello:RateLimiting")))
{
    identity.UseHmacRateLimiting(
        rateLimitKeys.CurrentVersion,
        rateLimitKeys.Keys);
}

using (var verificationKeys = VersionedSecretKeySet.Load(
    configuration.GetSection(
        "SkopkaHello:Verification")))
{
    var verificationKeyProvider =
        new StaticVerificationCodeKeyProvider(
            verificationKeys.CurrentVersion,
            verificationKeys.Keys);
    identity.UseHmacOneTimeCodes(verificationKeyProvider);
    identity.Services.RemoveAll<IVerificationCodeKeyProvider>();
    identity.Services.AddSingleton<IVerificationCodeKeyProvider>(
        _ => verificationKeyProvider);
}

identity.AddRoles();

identity.UseJwtBearerAuthentication(options =>
{
    options.ValidateSessionOnEveryRequest = configuration.GetValue(
        "SkopkaHello:Jwt:ValidateSessionOnEveryRequest",
        false);
});

var externalOidcSection = configuration.GetSection(
    "SkopkaHello:ExternalOidc");
builder.Services.AddSkopkaHelloOidc<HelloProfile>(options =>
{
    externalOidcSection.Bind(options);
    options.PublicOrigin = publicOrigin;
    options.SecureCookies = secureCookies;
    if (!secureCookies)
    {
        options.ExternalCookieName =
            "Skopka.Hello.External";
        options.PendingCookieName =
            "Skopka.Hello.External.Pending";
    }
});

var deliverySection = configuration.GetSection(
    "SkopkaHello:Delivery");
builder.Services.AddSkopkaHelloDelivery(
    options => deliverySection.Bind(options));
var smtpSection = deliverySection.GetSection("Smtp");
if (!string.IsNullOrWhiteSpace(smtpSection["Host"]))
{
    builder.Services.AddSkopkaHelloSmtpProvider(
        options => smtpSection.Bind(options));
}

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

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
var adminSection = configuration.GetSection("SkopkaHello:Admin");
builder.Services.AddSkopkaHelloAdmin<
    HelloProfile,
    HelloAdminProfileProjector>(options =>
{
    adminSection.Bind(options);
});
builder.Services.AddHostedService<
    IdentitySessionPruningWorker<HelloProfile>>();
var rateLimitPruningOptions = new IdentityRateLimitPruningOptions();
configuration.GetSection("SkopkaHello:RateLimitPruning")
    .Bind(rateLimitPruningOptions);
rateLimitPruningOptions.Validate();
builder.Services.AddSingleton(rateLimitPruningOptions);
builder.Services.AddHostedService<
    IdentityRateLimitPruningWorker<HelloProfile>>();

var app = builder.Build();
var serviceVersion = Assembly.GetEntryAssembly()?
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion
    ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
    ?? "unknown";

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Skopka.Hello API";
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Skopka.Hello API v1");
    });
}

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
            version = serviceVersion,
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
app.MapSkopkaHelloAdmin<HelloProfile>();
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

if (TryReadBootstrapAdminUserId(args, out var bootstrapAdminUserId))
{
    await BootstrapAdministratorAsync(
        app.Services,
        bootstrapAdminUserId,
        app.Lifetime.ApplicationStopping);
    return;
}

await app.RunAsync();

static bool TryReadBootstrapAdminUserId(
    string[] arguments,
    out Guid userId)
{
    userId = default;
    var index = Array.FindIndex(
        arguments,
        argument => string.Equals(
            argument,
            "--bootstrap-admin",
            StringComparison.Ordinal));
    if (index < 0)
    {
        return false;
    }

    if (index + 1 >= arguments.Length
        || !Guid.TryParse(arguments[index + 1], out userId)
        || userId == Guid.Empty)
    {
        throw new InvalidOperationException(
            "--bootstrap-admin requires a non-empty user id.");
    }

    return true;
}

static async Task BootstrapAdministratorAsync(
    IServiceProvider services,
    Guid userId,
    CancellationToken cancellationToken)
{
    await using var scope = services.CreateAsyncScope();
    var options = scope.ServiceProvider.GetRequiredService<
        SkopkaHelloAdminOptions>();
    var roles = scope.ServiceProvider.GetRequiredService<
        IIdentityRoleService<HelloProfile>>();
    var roleNames = new[]
        {
            options.ReadRoleName,
            options.ManageRoleName,
            options.DeleteRoleName,
        }
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    foreach (var roleName in roleNames)
    {
        var role = await roles.FindByNameAsync(
            roleName,
            cancellationToken);
        if (role is null)
        {
            var created = await roles.CreateAsync(
                new CreateRoleCommand(
                    roleName,
                    "Skopka.Hello administrator role."),
                cancellationToken);
            if (!created.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Could not create admin role '{roleName}': "
                    + string.Join(
                        ", ",
                        created.Errors.Select(error => error.Code)));
            }

            role = created.Value;
        }

        var membership = await roles.IsUserInRoleAsync(
            userId,
            role.Id,
            cancellationToken);
        if (!membership.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Could not inspect admin role '{roleName}': "
                + string.Join(
                    ", ",
                    membership.Errors.Select(error => error.Code)));
        }

        if (!membership.Value)
        {
            var assigned = await roles.AssignAsync(
                new AssignRoleCommand(userId, role.Id),
                cancellationToken);
            if (!assigned.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Could not assign admin role '{roleName}': "
                    + string.Join(
                        ", ",
                        assigned.Errors.Select(error => error.Code)));
            }
        }
    }

    var sessions = scope.ServiceProvider.GetRequiredService<
        IIdentitySessionService<HelloProfile>>();
    var revoked = await sessions.RevokeAllAsync(
        new RevokeAllIdentitySessionsCommand(userId),
        cancellationToken);
    if (!revoked.IsSuccess)
    {
        throw new InvalidOperationException(
            "Admin roles were assigned, but session revocation failed: "
            + string.Join(
                ", ",
                revoked.Errors.Select(error => error.Code)));
    }

    Console.WriteLine(
        $"Administrator roles assigned to user {userId:D}. Sign in again.");
}

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
