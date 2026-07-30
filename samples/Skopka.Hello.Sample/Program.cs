using Skopka.Hello.Endpoints;
using Skopka.Hello.Sample;
using Skopka.Hello.UI;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString(
        "Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required.");
var encodedKey = builder.Configuration[
        "SkopkaHello:Jwt:SigningKey"]
    ?? throw new InvalidOperationException(
        "SkopkaHello:Jwt:SigningKey is required.");
var signingKey = Convert.FromBase64String(encodedKey);
var secureCookies = builder.Configuration.GetValue(
    "SkopkaHello:Cookies:Secure",
    true);
var publicOrigin = new Uri(
    builder.Configuration["SkopkaHello:PublicOrigin"]
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
