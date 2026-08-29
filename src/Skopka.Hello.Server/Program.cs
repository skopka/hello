using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Skopka.Hello;
using Skopka.Hello.Admin;
using Skopka.Hello.AuthorizationServer;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Server;
using Skopka.Hello.UI;
using Skopka.Identity.Ef.PostgreSql;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Commands;
using Skopka.Identity.Sessions;
using Skopka.Identity.Totp;
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

var migrationCommand = args is ["--migrate"];
if (!migrationCommand
    && args.Contains("--migrate", StringComparer.Ordinal))
{
    throw new InvalidOperationException(
        "--migrate must be used as the only command-line argument.");
}

var builder = WebApplication.CreateBuilder(
    migrationCommand ? [] : args);
var configuration = builder.Configuration;

var connectionString = configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required.");
var publicOrigin = new Uri(
    configuration["SkopkaHello:PublicOrigin"]
        ?? throw new InvalidOperationException(
            "SkopkaHello:PublicOrigin is required."),
    UriKind.Absolute);
var authorizationSection = configuration.GetSection(
    "SkopkaHello:AuthorizationServer");
var authorizationEnabled = authorizationSection.GetValue(
    "Enabled",
    false);

if (migrationCommand)
{
    if (authorizationEnabled)
    {
        AddAuthorizationStorage(
            builder.Services,
            connectionString);
        builder.Services.AddSkopkaHelloAuthorizationClients(
            options => BindAuthorizationOptions(
                authorizationSection,
                options,
                publicOrigin));
    }

    await using var migrationApplication = builder.Build();
    await ApplyDatabaseMigrationsAsync(
        connectionString,
        authorizationEnabled,
        migrationApplication.Services,
        CancellationToken.None);
    return;
}

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
var persistenceOptions = new HelloServerPersistenceOptions();
configuration.GetSection("SkopkaHello:Persistence")
    .Bind(persistenceOptions);
persistenceOptions.Validate();
builder.Services.AddSingleton(persistenceOptions);
builder.Services.AddSingleton(_ =>
{
    var dataSourceConnection = new NpgsqlConnectionStringBuilder(
        connectionString)
    {
        CommandTimeout = checked((int)Math.Ceiling(
            persistenceOptions.CommandTimeout.TotalSeconds)),
        Timeout = checked((int)Math.Ceiling(
            persistenceOptions.CommandTimeout.TotalSeconds)),
    };
    return NpgsqlDataSource.Create(
        dataSourceConnection.ConnectionString);
});
builder.Services.AddSingleton<HelloProtectedPayloadSerializer>();

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
var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("Skopka.Hello");
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    dataProtection.PersistKeysToFileSystem(
        new DirectoryInfo(dataProtectionKeyPath));
}

var dataProtectionCertificatePath =
    configuration["SkopkaHello:DataProtection:CertificatePath"];
var dataProtectionDecryptionCertificates = configuration
    .GetSection(
        "SkopkaHello:DataProtection:DecryptionCertificates")
    .GetChildren()
    .ToArray();
if (string.IsNullOrWhiteSpace(dataProtectionCertificatePath)
    && dataProtectionDecryptionCertificates.Length > 0)
{
    throw new InvalidOperationException(
        "A current Data Protection certificate is required when decryption certificates are configured.");
}

if (!string.IsNullOrWhiteSpace(dataProtectionCertificatePath))
{
    var dataProtectionCertificates =
        new List<X509Certificate2>();
    try
    {
        var currentCertificate = LoadDataProtectionCertificate(
            dataProtectionCertificatePath,
            configuration[
                "SkopkaHello:DataProtection:CertificatePassword"]);
        dataProtectionCertificates.Add(currentCertificate);
        foreach (var certificateSection in
            dataProtectionDecryptionCertificates)
        {
            var path = certificateSection["Path"];
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    "Every Data Protection decryption certificate requires a path.");
            }

            dataProtectionCertificates.Add(
                LoadDataProtectionCertificate(
                    path,
                    certificateSection["Password"]));
        }

        dataProtection.ProtectKeysWithCertificate(currentCertificate);
        dataProtection.UnprotectKeysWithAnyCertificate(
            [.. dataProtectionCertificates]);
        foreach (var certificate in dataProtectionCertificates)
        {
            var ownedCertificate = certificate;
            builder.Services.AddSingleton(_ => ownedCertificate);
        }
    }
    catch
    {
        foreach (var certificate in dataProtectionCertificates)
        {
            certificate.Dispose();
        }

        throw;
    }
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
        options.Totp.Enabled = configuration.GetValue(
            "SkopkaHello:Totp:Enabled",
            true);
        options.Totp.Issuer = configuration[
                "SkopkaHello:Totp:Issuer"]
            ?? "Skopka.Hello";
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
    .UseDataProtectionTotp();

using (var jwtKeys = VersionedSecretKeySet.Load(
    configuration.GetSection("SkopkaHello:Jwt")))
{
    identity.UseJwtSessions(
        jwtKeys.CurrentVersion,
        jwtKeys.Keys,
        options =>
        {
            options.Issuer = issuer;
            options.Audience = audience;
        });
}

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

var crossDeviceSection = configuration.GetSection(
    "SkopkaHello:CrossDeviceSignIn");
var crossDeviceEnabled = crossDeviceSection.GetValue("Enabled", false);
if (crossDeviceEnabled)
{
    identity.AddCrossDeviceSignIn(options =>
    {
        crossDeviceSection.Bind(options);
        options.Enabled = true;
    });
}

if (authorizationEnabled)
{
    AddAuthorizationStorage(
        builder.Services,
        connectionString);
    builder.Services.AddSkopkaHelloAuthorizationServer<HelloProfile>(
        options => BindAuthorizationOptions(
            authorizationSection,
            options,
            publicOrigin),
        server =>
        {
            var signingPath = authorizationSection[
                "SigningCertificatePath"];
            var encryptionPath = authorizationSection[
                "EncryptionCertificatePath"];
            if (builder.Environment.IsDevelopment()
                && string.IsNullOrWhiteSpace(signingPath)
                && string.IsNullOrWhiteSpace(encryptionPath))
            {
                server.AddEphemeralSigningKey();
                server.AddEphemeralEncryptionKey();
                return;
            }

            if (string.IsNullOrWhiteSpace(signingPath)
                || string.IsNullOrWhiteSpace(encryptionPath))
            {
                throw new InvalidOperationException(
                    "Authorization Server signing and encryption certificates are required outside Development.");
            }

            var signingCertificate = LoadAuthorizationCertificate(
                signingPath,
                authorizationSection["SigningCertificatePassword"],
                "signing");
            var encryptionCertificate = LoadAuthorizationCertificate(
                encryptionPath,
                authorizationSection["EncryptionCertificatePassword"],
                "encryption");
            builder.Services.AddSingleton(signingCertificate);
            builder.Services.AddSingleton(encryptionCertificate);
            server.AddSigningCertificate(signingCertificate);
            server.AddEncryptionCertificate(encryptionCertificate);
        });
}

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
        options.LinkRequestCookieName =
            "Skopka.Hello.External.LinkRequest";
    }
});

var deliverySection = configuration.GetSection(
    "SkopkaHello:Delivery");
var deliveryOptions = new HelloDeliveryOptions();
deliverySection.Bind(deliveryOptions);
deliveryOptions.Validate();
var destinationEmailProviderId = deliveryOptions.EmailProviderId;
var durableEmailEnabled =
    persistenceOptions.DurableDeliveryEnabled
    && !string.IsNullOrWhiteSpace(destinationEmailProviderId);
builder.Services.AddSkopkaHelloDelivery(options =>
{
    deliverySection.Bind(options);
    if (durableEmailEnabled)
    {
        options.EmailProviderId =
            PostgreSqlHelloAccountMessageOutbox
                .DurableEmailProviderId;
    }
});
var smtpSection = deliverySection.GetSection("Smtp");
if (!string.IsNullOrWhiteSpace(smtpSection["Host"]))
{
    builder.Services.AddSkopkaHelloSmtpProvider(options =>
    {
        smtpSection.Bind(options);
        var localization = smtpSection.GetSection("Localization");
        options.Localization.DefaultCulture =
            localization["DefaultCulture"] ?? "en";
        foreach (var dictionary in localization
                     .GetSection("Dictionaries")
                     .GetChildren())
        {
            var culture = dictionary["Culture"]
                ?? throw new InvalidOperationException(
                    "SkopkaHello:Delivery:Smtp:Localization:Dictionaries entries require Culture.");
            var filePath = dictionary["FilePath"]
                ?? throw new InvalidOperationException(
                    "SkopkaHello:Delivery:Smtp:Localization:Dictionaries entries require FilePath.");
            options.Localization.AddDictionaryFile(
                culture,
                filePath);
        }

        options.UseBackgroundQueue = !durableEmailEnabled;
    });
}

if (persistenceOptions.DurableDeliveryEnabled)
{
    builder.Services.Replace(
        ServiceDescriptor.Singleton<
            IHelloAnonymousAccountMessageInbox,
            PostgreSqlHelloAnonymousAccountMessageInbox>());
}

if (durableEmailEnabled)
{
    var routeProviderId = HelloAccountMessageDispatcher
        .NormalizeProviderId(
            destinationEmailProviderId,
            "The durable email destination provider id");
    if (string.Equals(
        routeProviderId,
        PostgreSqlHelloAccountMessageOutbox
            .DurableEmailProviderId,
        StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "The durable email destination cannot reference the outbox provider itself.");
    }

    builder.Services.AddSingleton(
        new HelloDurableEmailRouteOptions(routeProviderId));
    builder.Services.AddSkopkaHelloEmailProvider<
        PostgreSqlHelloAccountMessageOutbox>();
    builder.Services.AddHostedService<
        PostgreSqlHelloAccountMessageWorker>();
}

if (persistenceOptions.AuditEnabled)
{
    builder.Services.AddSingleton<PostgreSqlHelloAuditOutbox>();
    builder.Services.Replace(
        ServiceDescriptor.Singleton<IHelloSecurityEventSink>(provider =>
            provider.GetRequiredService<
                PostgreSqlHelloAuditOutbox>()));
    builder.Services.AddSingleton<IHelloAuditOutbox>(provider =>
        provider.GetRequiredService<PostgreSqlHelloAuditOutbox>());
}

if (persistenceOptions.DurableDeliveryEnabled
    || persistenceOptions.AuditEnabled)
{
    builder.Services.AddHostedService<
        HelloServerPersistencePruningWorker>();
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
    options.TermsOfServiceUrl =
        configuration["SkopkaHello:Ui:TermsOfServiceUrl"];
    options.PrivacyPolicyUrl =
        configuration["SkopkaHello:Ui:PrivacyPolicyUrl"];
    options.NoticeText =
        configuration["SkopkaHello:Ui:NoticeText"];
    options.CustomCssFilePath =
        configuration["SkopkaHello:Customization:CssFilePath"];
    options.CustomCssRequestPath =
        configuration["SkopkaHello:Customization:CssRequestPath"]
        ?? SkopkaHelloUiOptions.DefaultCustomCssRequestPath;
    options.BuiltInStylesEnabled = configuration.GetValue(
        "SkopkaHello:Customization:BuiltInStylesEnabled",
        true);
    configuration
        .GetSection("SkopkaHello:Ui:Registration")
        .Bind(options.Registration);
    var localization = configuration.GetSection(
        "SkopkaHello:Ui:Localization");
    options.Localization.Enabled = localization.GetValue(
        "Enabled",
        false);
    options.Localization.DefaultCulture =
        localization["DefaultCulture"] ?? "en";
    foreach (var culture in localization
                 .GetSection("Cultures")
                 .GetChildren())
    {
        var name = culture["Name"]
            ?? throw new InvalidOperationException(
                "SkopkaHello:Ui:Localization:Cultures entries require Name.");
        var displayName = culture["DisplayName"];
        if (displayName is not null)
        {
            options.Localization.AddCulture(name, displayName);
        }

        foreach (var dictionaryFile in culture
                     .GetSection("DictionaryFiles")
                     .Get<string[]>() ?? [])
        {
            options.Localization.AddDictionaryFile(
                name,
                dictionaryFile,
                displayName);
        }
    }
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
if (crossDeviceEnabled)
{
    builder.Services.AddHostedService<
        IdentityDeviceAuthorizationPruningWorker<HelloProfile>>();
}

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
if (authorizationEnabled)
{
    app.MapGet(
        "/health/ready",
        async (
            PostgreSqlIdentityDbContext<HelloProfile> database,
            HelloAuthorizationDbContext authorizationDatabase,
            NpgsqlDataSource helloDataSource,
            CancellationToken cancellationToken) =>
            await database.Database.CanConnectAsync(cancellationToken)
                && !((await database.Database.GetPendingMigrationsAsync(
                        cancellationToken))
                    .Any())
                && !((await authorizationDatabase.Database
                        .GetPendingMigrationsAsync(cancellationToken))
                    .Any())
                && await HelloServerDatabaseMigrator.IsCurrentAsync(
                    helloDataSource,
                    cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(
                    StatusCodes.Status503ServiceUnavailable));
}
else
{
    app.MapGet(
        "/health/ready",
        async (
            PostgreSqlIdentityDbContext<HelloProfile> database,
            NpgsqlDataSource helloDataSource,
            CancellationToken cancellationToken) =>
            await database.Database.CanConnectAsync(cancellationToken)
                && !((await database.Database.GetPendingMigrationsAsync(
                        cancellationToken))
                    .Any())
                && await HelloServerDatabaseMigrator.IsCurrentAsync(
                    helloDataSource,
                    cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(
                    StatusCodes.Status503ServiceUnavailable));
}
app.MapSkopkaHello<HelloProfile>();
app.MapSkopkaHelloAdmin<HelloProfile>();
app.MapSkopkaHelloUi();
if (authorizationEnabled)
{
    app.MapSkopkaHelloAuthorizationServer<HelloProfile>();
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

static async Task ApplyDatabaseMigrationsAsync(
    string connectionString,
    bool authorizationEnabled,
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    var options = new DbContextOptionsBuilder<
            PostgreSqlIdentityDbContext<HelloProfile>>()
        .UseNpgsql(connectionString)
        .Options;
    await using var database =
        new PostgreSqlIdentityDbContext<HelloProfile>(options);
    var pending = (await database.Database
            .GetPendingMigrationsAsync(cancellationToken))
        .ToArray();

    if (pending.Length > 0)
    {
        await database.Database.MigrateAsync(cancellationToken);
        if ((await database.Database
                .GetPendingMigrationsAsync(cancellationToken))
            .Any())
        {
            throw new InvalidOperationException(
                "Identity database migrations did not complete.");
        }
    }

    var helloApplied = await HelloServerDatabaseMigrator.ApplyAsync(
        connectionString,
        cancellationToken);

    var authorizationApplied = 0;
    if (authorizationEnabled)
    {
        await using var scope = services.CreateAsyncScope();
        var authorizationDatabase = scope.ServiceProvider
            .GetRequiredService<HelloAuthorizationDbContext>();
        var authorizationPending = (await authorizationDatabase.Database
                .GetPendingMigrationsAsync(cancellationToken))
            .ToArray();
        if (authorizationPending.Length > 0)
        {
            await authorizationDatabase.Database.MigrateAsync(
                cancellationToken);
        }

        if ((await authorizationDatabase.Database
                .GetPendingMigrationsAsync(cancellationToken))
            .Any())
        {
            throw new InvalidOperationException(
                "Authorization Server database migrations did not complete.");
        }

        var clients = scope.ServiceProvider.GetRequiredService<
            IHelloAuthorizationClientSynchronizer>();
        await clients.SynchronizeAsync(cancellationToken);
        authorizationApplied = authorizationPending.Length;
    }

    Console.WriteLine(
        $"Database is current. Applied {pending.Length} Identity, {helloApplied} Hello and {authorizationApplied} Authorization Server migration(s). Authorization clients are current.");
}

static void AddAuthorizationStorage(
    IServiceCollection services,
    string connectionString)
{
    services.AddDbContext<HelloAuthorizationDbContext>(options =>
    {
        options.UseNpgsql(
            connectionString,
            HelloAuthorizationDbContext.ConfigureNpgsql);
        options.UseOpenIddict();
    });
    services.AddOpenIddict()
        .AddCore(options => options.UseEntityFrameworkCore()
            .UseDbContext<HelloAuthorizationDbContext>());
}

static void BindAuthorizationOptions(
    IConfigurationSection section,
    HelloAuthorizationServerOptions options,
    Uri defaultIssuer)
{
    section.Bind(options);
    options.Issuer ??= defaultIssuer;
}

static X509Certificate2 LoadDataProtectionCertificate(
    string path,
    string? password)
{
    var certificate = X509CertificateLoader.LoadPkcs12FromFile(
        path,
        password,
        X509KeyStorageFlags.EphemeralKeySet);
    if (certificate.HasPrivateKey)
    {
        return certificate;
    }

    certificate.Dispose();
    throw new InvalidOperationException(
        $"The Data Protection certificate '{path}' must contain a private key.");
}

static X509Certificate2 LoadAuthorizationCertificate(
    string path,
    string? password,
    string purpose)
{
    var certificate = X509CertificateLoader.LoadPkcs12FromFile(
        path,
        password,
        X509KeyStorageFlags.EphemeralKeySet);
    if (certificate.HasPrivateKey)
    {
        return certificate;
    }

    certificate.Dispose();
    throw new InvalidOperationException(
        $"The Authorization Server {purpose} certificate '{path}' must contain a private key.");
}

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

public partial class Program;
