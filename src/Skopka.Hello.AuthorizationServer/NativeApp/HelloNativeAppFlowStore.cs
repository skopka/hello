using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skopka.Hello.AuthorizationServer;

/// <summary>Durable, purpose-separated native sign-in state. Secrets are hashes or protected data.</summary>
public sealed class HelloNativeAppFlow
{
    public string Id { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public long ClientRevision { get; set; }
    public string Callback { get; set; } = null!;
    public string State { get; set; } = null!;
    public string Challenge { get; set; } = null!;
    public string WebViewBinderHash { get; set; } = null!;
    public string LaunchSecretHash { get; set; } = null!;
    public string? BrowserBinderHash { get; set; }
    public string? CompletionReceiptHash { get; set; }
    public string? ProtectedSource { get; set; }
    public string? ReturnUrl { get; set; }
    public string SessionMetadata { get; set; } = null!;
    public HelloNativeAppSignInState Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LaunchExpiresAt { get; set; }
    public DateTimeOffset? ApprovalExpiresAt { get; set; }
    public long Version { get; set; }
}

public static class HelloNativeAppModelBuilderExtensions
{
    public static ModelBuilder AddHelloNativeAppFlows(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        EntityTypeBuilder<HelloNativeAppFlow> entity = modelBuilder.Entity<HelloNativeAppFlow>();
        entity.ToTable("native_app_flows", "skopka_hello");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasMaxLength(64);
        entity.Property(x => x.ClientId).HasMaxLength(200);
        entity.Property(x => x.Callback).HasMaxLength(2048);
        entity.Property(x => x.State).HasMaxLength(1024);
        entity.Property(x => x.Challenge).HasMaxLength(43);
        entity.Property(x => x.WebViewBinderHash).HasMaxLength(64);
        entity.Property(x => x.LaunchSecretHash).HasMaxLength(64);
        entity.Property(x => x.BrowserBinderHash).HasMaxLength(64);
        entity.Property(x => x.CompletionReceiptHash).HasMaxLength(64);
        entity.Property(x => x.ReturnUrl).HasMaxLength(2048);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.CreatedAt).HasConversion<long>();
        entity.Property(x => x.ExpiresAt).HasConversion<long>();
        entity.Property(x => x.LaunchExpiresAt).HasConversion<long>();
        entity.Property(x => x.ApprovalExpiresAt).HasConversion<long>();
        entity.HasIndex(x => x.ExpiresAt);
        return modelBuilder;
    }
}

public interface IHelloNativeAppFlowStore
{
    Task<int> PruneAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
    Task AddAsync(HelloNativeAppFlow flow, CancellationToken cancellationToken);
    Task<HelloNativeAppFlow?> FindAsync(string id, CancellationToken cancellationToken);
    /// <summary>Atomically replaces the row only if the previous version still matches.</summary>
    Task<bool> ReplaceAsync(HelloNativeAppFlow flow, long expectedVersion, CancellationToken cancellationToken);
}

public sealed class EfHelloNativeAppFlowStore<TContext>(TContext context) : IHelloNativeAppFlowStore where TContext : DbContext
{
    public async Task<int> PruneAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
        var ids = await context.Set<HelloNativeAppFlow>()
            .Where(x => x.ExpiresAt < cutoff)
            .OrderBy(x => x.ExpiresAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        return await context.Set<HelloNativeAppFlow>()
            .Where(x => ids.Contains(x.Id) && x.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddAsync(HelloNativeAppFlow flow, CancellationToken cancellationToken)
    {
        context.Set<HelloNativeAppFlow>().Add(flow);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(flow).State = EntityState.Detached;
    }

    public Task<HelloNativeAppFlow?> FindAsync(string id, CancellationToken cancellationToken)
        => context.Set<HelloNativeAppFlow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> ReplaceAsync(HelloNativeAppFlow flow, long expectedVersion, CancellationToken cancellationToken)
    {
        flow.Version = checked(expectedVersion + 1);
        context.Attach(flow);
        context.Entry(flow).State = EntityState.Modified;
        context.Entry(flow).Property(x => x.Version).OriginalValue = expectedVersion;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            context.Entry(flow).State = EntityState.Detached;
        }
    }
}
