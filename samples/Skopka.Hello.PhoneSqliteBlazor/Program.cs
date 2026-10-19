using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Skopka.Hello;
using Skopka.Hello.Endpoints;
using Skopka.Hello.PhoneSqliteBlazor;
using Skopka.Hello.PhoneSqliteBlazor.Components;
using Skopka.Hello.UI;
using Skopka.Identity.Ef.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var development = builder.Environment.IsDevelopment();
var identityDatabase = builder.Configuration.GetConnectionString("Identity")
    ?? "Data Source=phone-sample.db";
var identity = builder.Services.AddSkopkaHello<SampleProfile>(options =>
{
    options.PhoneOtpEnabled = true;
    options.PasswordSignInEnabled = false;
    options.ClientName = "Four Knights sample";
    options.SmsApplicationName = "Четыре коня";
    options.SecureCookies = !development;
    if (development)
    {
        options.RefreshCookieName = "Skopka.Hello.Refresh";
        options.AntiforgeryCookieName = "Skopka.Hello.Antiforgery";
        options.AntiforgeryRequestCookieName = "Skopka.Hello.XSRF-TOKEN";
    }
})
    .ConfigurePhoneOtp(options => options.AllowedCountryCodes = ["+7"])
    .UseSqlite(identityDatabase)
    .UseHmacOneTimeCodes("sample", ReadKey("VerificationKey"))
    .UseHmacRateLimiting(ReadKey("RateLimitKey"))
    .UseJwtSessions(ReadKey("JwtKey"), options =>
    {
        options.Issuer = "https://phone-sample.local";
        options.Audience = "phone-sample";
    });

identity.UseJwtBearerAuthentication(options =>
    options.SetAsDefaultScheme = false);

if (development)
{
    builder.Services.AddDevelopmentLoggingHelloSmsProvider(
        builder.Environment);
    builder.Services.AddSkopkaHelloDelivery(options =>
        options.SmsProviderId = DevelopmentLoggingHelloSmsProvider.Id);
}

builder.Services.AddSkopkaHelloUi<SampleProfile, SampleProfileFactory>(
    options =>
    {
        options.EnabledPages = HelloUiPages.Login
            | HelloUiPages.Account
            | HelloUiPages.Sessions;
        options.LayoutPath = "/Pages/Shared/_HostLayout.cshtml";
        options.Localization.Enabled = true;
        options.Localization.DefaultCulture = "ru";
        options.SecureCookies = !development;
        if (development)
        {
            options.AuthenticationCookieName = "Skopka.Hello.UI";
            options.AccountSwitching.CookieName = "Skopka.Hello.Accounts";
        }
    });

// Cookie requests from static Blazor pages and Bearer API requests share the
// same Identity logical sessions. Hello's UI cookie validates online.
builder.Services.AddAuthentication(options =>
    options.DefaultScheme = "sample-auth")
    .AddPolicyScheme("sample-auth", null, options =>
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? "Bearer"
                : HelloUiDefaults.AuthenticationScheme);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents();

var app = builder.Build();
if (development)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider
        .GetRequiredService<SqliteIdentityDbContext<SampleProfile>>()
        .Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapSkopkaHello<SampleProfile>();
app.MapSkopkaHelloUi();
app.MapRazorComponents<App>();
await app.RunAsync();

byte[] ReadKey(string name)
{
    var encoded = builder.Configuration[$"PhoneSample:{name}"];
    if (!string.IsNullOrWhiteSpace(encoded))
    {
        var key = Convert.FromBase64String(encoded);
        if (key.Length >= 32)
        {
            return key;
        }
    }

    if (development)
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    throw new InvalidOperationException(
        $"PhoneSample:{name} must contain at least 32 random bytes in Base64.");
}
