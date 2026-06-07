using System.Linq.Expressions;
using Matloob.Domain.Admins;
using Matloob.Domain.Applications;
using Matloob.Domain.Assets;
using Matloob.Domain.Auditing;
using Matloob.Domain.Common;
using Matloob.Domain.Establishments;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Events;
using Matloob.Domain.Notifications;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Matloob.Domain.Users;
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

    // Back-office admin users (local mirror of IdM admin identities)
    public DbSet<Admin> Admins => Set<Admin>();

    // Reference / lookup data
    public DbSet<City> Cities => Set<City>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<District> Districts => Set<District>();
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

    // Uploaded files (bytes live in IFileStorage; this is the metadata table).
    public DbSet<Asset> Assets => Set<Asset>();

    // Polymorphic attachment links (Asset <-> any owner via model_type/model_id).
    public DbSet<Media> Media => Set<Media>();

    // Establishment onboarding aggregate + sibling entities.
    public DbSet<Establishment> Establishments => Set<Establishment>();
    public DbSet<EstablishmentDocument> EstablishmentDocuments => Set<EstablishmentDocument>();
    public DbSet<EstablishmentMember> EstablishmentMembers => Set<EstablishmentMember>();
    public DbSet<EstablishmentInvitation> EstablishmentInvitations => Set<EstablishmentInvitation>();
    public DbSet<EstablishmentChangeRequest> EstablishmentChangeRequests => Set<EstablishmentChangeRequest>();

    // Establishment profile children: services + products offered.
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<EstablishmentExperience> EstablishmentExperiences => Set<EstablishmentExperience>();

    // Establishment events + their opportunity-category pivot.
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventOpportunityCategory> EventOpportunityCategories
        => Set<EventOpportunityCategory>();

    // Append-only audit. Not soft-deletable -- inherits BaseEntity, not BaseAuditableEntity.
    public DbSet<EstablishmentReviewHistory> EstablishmentReviewHistory => Set<EstablishmentReviewHistory>();

    // Transactional outbox. Written in the same SaveChanges as the aggregate
    // change that triggered the event; drained by the dispatcher background
    // service.
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    // Delivered in-app notifications (populated by the outbox fanout subscriber).
    public DbSet<Notification> Notifications => Set<Notification>();

    // Local cache of IdM users. Created/updated by ICurrentUserSyncService
    // on authenticated requests.
    public DbSet<User> Users => Set<User>();

    // User-profile child collections (migrated from the Laravel profile tables).
    public DbSet<UserEducation> UserEducation => Set<UserEducation>();
    public DbSet<UserExperience> UserExperiences => Set<UserExperience>();
    public DbSet<UserCertificate> UserCertificates => Set<UserCertificate>();
    public DbSet<UserSkill> UserSkills => Set<UserSkill>();
    public DbSet<UserLanguageProficiency> UserLanguages => Set<UserLanguageProficiency>();
    public DbSet<UserProfession> UserProfessions => Set<UserProfession>();
    public DbSet<SupportiveDocument> SupportiveDocuments => Set<SupportiveDocument>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    // Opportunities aggregate + side tables (assets + success-management
    // criteria + criterion assets).
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<OpportunityAsset> OpportunityAssets => Set<OpportunityAsset>();
    public DbSet<SuccessManagementCriterion> SuccessManagementCriteria
        => Set<SuccessManagementCriterion>();
    public DbSet<SuccessManagementCriterionAsset> SuccessManagementCriterionAssets
        => Set<SuccessManagementCriterionAsset>();

    // Applications to opportunities (both worker-side and establishment-side).
    public DbSet<OpportunityApplication> OpportunityApplications
        => Set<OpportunityApplication>();

    // Offers + their cancellation-request child rows.
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferCancellationRequest> OfferCancellationRequests
        => Set<OfferCancellationRequest>();

    // Evaluations (attached to offers, not contracts — Q-EVAL-1).
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<EvaluationAsset> EvaluationAssets => Set<EvaluationAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Discover EF configurations placed next to entities in feature folders.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global soft-delete query filter for every ISoftDeletable entity.
        // Hides rows with IsDeleted = true from all standard LINQ queries.
        // Bypass with .IgnoreQueryFilters() when restoring or admin-listing.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Query filters can only be set on the root of an inheritance
            // hierarchy (TPH); skip derived types (e.g. Admin : User) — the
            // filter on the root (User) already covers them.
            if (entityType.BaseType == null
                && typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
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
