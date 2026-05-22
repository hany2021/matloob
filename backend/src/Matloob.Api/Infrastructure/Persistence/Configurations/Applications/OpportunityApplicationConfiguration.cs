using Matloob.Domain.Applications;
using Matloob.Domain.Establishments;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Applications;

/// <summary>
/// EF mapping for <see cref="OpportunityApplication"/>. Replaces the
/// legacy polymorphic <c>applier</c> with split FKs and adds:
/// <list type="bullet">
/// <item>a CHECK constraint enforcing exactly one applicant target;</item>
/// <item>partial unique indexes preventing double-apply per
///   (opportunity, applicant).</item>
/// </list>
///
/// Soft-delete query filter is applied centrally by AppDbContext.
/// </summary>
internal sealed class OpportunityApplicationConfiguration
    : IEntityTypeConfiguration<OpportunityApplication>
{
    public void Configure(EntityTypeBuilder<OpportunityApplication> builder)
    {
        builder.ToTable("opportunity_applications", t =>
        {
            // Exactly one of (applicant_user_id, applicant_establishment_id)
            // must be set. A bare null+null row is meaningless and so is a
            // both-set row.
            t.HasCheckConstraint(
                "ck_opportunity_applications_applicant_one_of",
                "(applicant_user_id IS NOT NULL AND applicant_establishment_id IS NULL) " +
                "OR (applicant_user_id IS NULL AND applicant_establishment_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ApplicantUserId).HasMaxLength(200);
        builder.Property(x => x.AppliedByUserId).HasMaxLength(200);

        // --- Relationships --------------------------------------------------
        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.ApplicantEstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Indexes --------------------------------------------------------
        builder.HasIndex(x => x.OpportunityId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_opportunity_applications_opportunity");

        // Partial unique: one active application per (opportunity, user
        // applicant). Prevents the worker from applying twice. Matches
        // Laravel's implicit semantics (the apply controller checked for
        // existing applicants before insert; this enforces it at the DB).
        builder.HasIndex(x => new { x.OpportunityId, x.ApplicantUserId })
            .IsUnique()
            .HasFilter("applicant_user_id IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ux_opportunity_applications_user_active");

        // Partial unique: one active application per (opportunity,
        // establishment applicant).
        builder.HasIndex(x => new { x.OpportunityId, x.ApplicantEstablishmentId })
            .IsUnique()
            .HasFilter("applicant_establishment_id IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ux_opportunity_applications_establishment_active");
    }
}
