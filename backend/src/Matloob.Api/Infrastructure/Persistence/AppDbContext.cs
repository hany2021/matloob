using System.Linq.Expressions;
using Matloob.Domain.Auditing;
using Matloob.Domain.Common;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for the whole API. Feature slices contribute their
/// IEntityTypeConfiguration<T> via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>
/// so this class never needs to import slice-specific types.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    // Reference / lookup data
    public DbSet<City> Cities => Set<City>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<JobTitle> JobTitles => Set<JobTitle>();
    public DbSet<EventType> EventTypes => Set<EventType>();
    public DbSet<OpportunityCategory> OpportunityCategories => Set<OpportunityCategory>();
    public DbSet<OfferCancellationReason> OfferCancellationReasons => Set<OfferCancellationReason>();
    public DbSet<OfferRejectionReason> OfferRejectionReasons => Set<OfferRejectionReason>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SuggestedLocation> SuggestedLocations => Set<SuggestedLocation>();
    public DbSet<SuggestedAttendee> SuggestedAttendees => Set<SuggestedAttendee>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Translation> Translations => Set<Translation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Discover EF configurations placed next to entities in feature folders.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global soft-delete query filter for every ISoftDeletable entity.
        // Hides rows with IsDeleted = true from all standard LINQ queries.
        // Bypass with .IgnoreQueryFilters() when restoring or admin-listing.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var prop = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                var notDeleted = Expression.Not(prop);
                var lambda = Expression.Lambda(notDeleted, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
