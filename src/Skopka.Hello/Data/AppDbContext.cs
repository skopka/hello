using Microsoft.EntityFrameworkCore;

namespace Skopka.Hello.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // TODO: add entities and configurations (Users, Credentials, Clients, ClientKeys, Consents, Scopes, RefreshTokens, Keys, Audit)
    }
}
