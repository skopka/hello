using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Hello;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Sample;
using Skopka.Hello.UI;
using Skopka.Identity.Verification;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var connectionString = configuration.GetConnectionString(
        "Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required.");
var encodedKey = configuration[
        "SkopkaHello:Jwt:SigningKey"]
    ?? throw new InvalidOperationException(
        "SkopkaHello:Jwt:SigningKey is required.");
var signingKey = Convert.FromBase64String(encodedKey);
var secureCookies = configuration.GetValue(
    "SkopkaHello:Cookies:Secure",
    true);
var publicOrigin = new Uri(
    configuration["SkopkaHello:PublicOrigin"]
        ?? "https://localhost:8443",
    UriKind.Absolute);

var identity = builder.Services
    .AddSkopkaHello<SampleProfile>(options =>
    {
        options.ClientName = "Skopka.Hello.Sample";
        options.SecureCookies = secureCookies;
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
    .UsePostgreSql(connectionString)
    .UsePbkdf2PasswordHasher()
    .UseDataProtectionActionTokens()
    .UseJwtSessions(
        signingKey,
        options =>
        {
            options.Issuer = "https://sample.skopka.local";
            options.Audience = "skopka-hello-sample";
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

identity.UseJwtBearerAuthentication();
builder.Services.AddProblemDetails();
builder.Services.AddSkopkaHelloUi<
    SampleProfile,
    SampleProfileUiFactory>(options =>
{
    options.SecureCookies = secureCookies;
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
app.MapSkopkaHello<SampleProfile>();
app.MapSkopkaHelloUi();
await app.RunAsync();
