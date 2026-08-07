using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using OpenIddict.EntityFrameworkCore.Models;

namespace Skopka.Hello.Server;

public sealed class HelloAuthorizationDbContext(
    DbContextOptions<HelloAuthorizationDbContext> options)
    : DbContext(options)
{
    internal const string SchemaName = "skopka_hello_oauth";
    internal const string MigrationsHistoryTableName =
        "schema_migrations";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict();
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication>()
            .ToTable("applications", SchemaName);
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization>()
            .ToTable("authorizations", SchemaName);
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope>()
            .ToTable("scopes", SchemaName);
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken>()
            .ToTable("tokens", SchemaName);
    }

    internal static void ConfigureNpgsql(
        NpgsqlDbContextOptionsBuilder options)
        => options.MigrationsHistoryTable(
            MigrationsHistoryTableName,
            SchemaName);
}

public sealed class HelloAuthorizationDbContextFactory
    : IDesignTimeDbContextFactory<HelloAuthorizationDbContext>
{
    public HelloAuthorizationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<
                HelloAuthorizationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=skopka_hello;Username=skopka;Password=design-time",
                HelloAuthorizationDbContext.ConfigureNpgsql)
            .UseOpenIddict()
            .Options;
        return new HelloAuthorizationDbContext(options);
    }
}
