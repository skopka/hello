using Skopka.Hello.Endpoints;
using Skopka.Hello.Sample;

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

var identity = builder.Services
    .AddSkopkaHello<SampleProfile>(options =>
    {
        options.ClientName = "Skopka.Hello.Sample";
        options.SecureCookies = builder.Configuration.GetValue(
            "SkopkaHello:Cookies:Secure",
            true);
    })
    .UsePostgreSql(connectionString)
    .UsePbkdf2PasswordHasher()
    .UseJwtSessions(
        signingKey,
        options =>
        {
            options.Issuer = "https://sample.skopka.local";
            options.Audience = "skopka-hello-sample";
        });

identity.UseJwtBearerAuthentication();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapSkopkaHello<SampleProfile>();
await app.RunAsync();
