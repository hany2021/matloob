using Matloob.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for the whole API. Feature slices contribute their
/// IEntityTypeConfiguration<T> via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>
/// so this class never needs to import slice-specific types.
///
/// No DbSet&lt;T&gt; is declared here yet. Entities are added by feature slices
/// from the next commit onward (AuditEntry first).
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Discover EF configurations placed next to entities in feature folders.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global soft-delete query filter is applied per entity in its
        // IEntityTypeConfiguration<T> (via HasQueryFilter on the concrete CLR
        // type). Applying it generically over ISoftDeletable requires
        // reflection over modelBuilder.Model.GetEntityTypes() and is deferred
        // until the first soft-deletable business entity arrives.

        base.OnModelCreating(modelBuilder);
    }
}
