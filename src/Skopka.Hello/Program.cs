using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// External config first: /config/settings/appsettings.json + environment variables
builder.Configuration
    .AddJsonFile("/config/settings/appsettings.json", optional: true, reloadOnChange: true)
    .AddKeyPerFile("/config/secret")
    .AddEnvironmentVariables();

var configuration = builder.Configuration;

// Configuration from environment variables (.env can be used with docker compose)
// Expected keys: CONNECTIONSTRINGS__DEFAULT, REDIS_CONNECTION
var dbConnection = configuration["ConnectionStrings:Default"] ?? configuration["CONNECTIONSTRINGS__DEFAULT"];
var redisConnection = configuration["REDIS_CONNECTION"];

// EF Core: register DbContext (Npgsql)
if (!string.IsNullOrWhiteSpace(dbConnection))
{
    builder.Services.AddDbContext<Skopka.Hello.Data.AppDbContext>(options =>
        options.UseNpgsql(dbConnection));
}

// Add health checks, Swagger, and minimal services baseline
builder.Services
    .AddHealthChecks()
    .AddNpgSql(dbConnection ?? string.Empty, name: "postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(3), failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy)
    .AddRedis(redisConnection ?? string.Empty, name: "redis", tags: ["ready"], failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Migration runner (separate container recommended)
bool GetBool(string key) => string.Equals(configuration[key], "true", StringComparison.OrdinalIgnoreCase);
if (GetBool("MIGRATIONS__APPLY"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Skopka.Hello.Data.AppDbContext>();
    await db.Database.MigrateAsync();
    if (GetBool("MIGRATIONS__EXITAFTER"))
    {
        // Exit after applying migrations (used by db-migrator service)
        return;
    }
}

// Enable Swagger in Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => "Hello World!");

// Admin ping (dev-only) protected by API key
if (app.Environment.IsDevelopment())
{
    app.MapGet("/admin/ping", (HttpContext http) =>
    {
        var configuredKey = configuration["ADMIN_API_KEY"];
        if (string.IsNullOrEmpty(configuredKey)) return Results.Unauthorized();
        if (!http.Request.Headers.TryGetValue("X-API-Key", out var provided) || provided != configuredKey)
        {
            return Results.Unauthorized();
        }
        return Results.Ok(new { status = "ok" });
    });
}

// Health checks
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

app.Run();