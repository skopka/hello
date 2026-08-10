using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Hello;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Oidc;
using Skopka.Hello.Sample;
using Skopka.Hello.UI;
using Skopka.Identity.Verification;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var connectionString = configuration.GetConnectionString(
        "Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required.");
var secureCookies = configuration.GetValue(
    "SkopkaHello:Cookies:Secure",
    true);
var publicOrigin = new Uri(
    configuration["SkopkaHello:PublicOrigin"]
        ?? "https://localhost:8443",
    UriKind.Absolute);
var selfRegistrationEnabled = configuration.GetValue(
    "SkopkaHello:SelfRegistration:Enabled",
    true);
var uiPathPrefix = configuration[
        "SkopkaHello:Ui:PathPrefix"]
    ?? HelloUiRoutePaths.DefaultPathPrefix;

var identity = builder.Services
    .AddSkopkaHello<SampleProfile>(options =>
    {
        options.ClientName = "Skopka.Hello.Sample";
        options.SecureCookies = secureCookies;
        options.PublicOrigin = publicOrigin;
        options.SelfRegistrationEnabled = selfRegistrationEnabled;
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
    .UsePostgreSql(connectionString)
    .UsePbkdf2PasswordHasher()
    .UseDataProtectionActionTokens();

using (var jwtKeys = VersionedSecretKeySet.Load(
    configuration.GetSection("SkopkaHello:Jwt")))
{
    identity.UseJwtSessions(
        jwtKeys.CurrentVersion,
        jwtKeys.Keys,
        options =>
        {
            options.Issuer = "https://sample.skopka.local";
            options.Audience = "skopka-hello-sample";
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

identity.UseJwtBearerAuthentication();
var externalOidcSection = configuration.GetSection(
    "SkopkaHello:ExternalOidc");
builder.Services.AddSkopkaHelloOidc<SampleProfile>(options =>
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
builder.Services.AddProblemDetails();
builder.Services.AddSkopkaHelloUi<
    SampleProfile,
    SampleProfileUiFactory>(options =>
{
    options.SecureCookies = secureCookies;
    options.Localization.Enabled = configuration.GetValue(
        "SkopkaHello:Ui:Localization:Enabled",
        false);
    options.Localization.DefaultCulture = configuration[
            "SkopkaHello:Ui:Localization:DefaultCulture"]
        ?? "en";
    if (!secureCookies)
    {
        options.AuthenticationCookieName =
            "Skopka.Hello.UI";
    }
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapGet(
    "/app/config",
    (SkopkaHelloOptions options) => Results.Ok(new
    {
        antiforgeryCookieName =
            options.AntiforgeryRequestCookieName,
        antiforgeryHeaderName = options.AntiforgeryHeaderName,
        oidcReturnPath = "/app/oidc-return",
    }));
app.MapSkopkaHello<SampleProfile>();
app.MapSkopkaHelloUi();
app.MapFallbackToFile(
    "/app/{*path:nonfile}",
    "app/index.html");
await app.RunAsync();
