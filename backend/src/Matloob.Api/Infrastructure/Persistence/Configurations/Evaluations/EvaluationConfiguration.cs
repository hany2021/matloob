using Matloob.Domain.Establishments;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Evaluations;

internal sealed class EvaluationConfiguration : IEntityTypeConfiguration<Evaluation>
{
    public void Configure(EntityTypeBuilder<Evaluation> builder)
    {
        builder.ToTable("evaluations", t =>
        {
            // Exactly one evaluable target.
            t.HasCheckConstraint(
                "ck_evaluations_evaluable_one_of",
                "(evaluable_user_id IS NOT NULL AND evaluable_establishment_id IS NULL) " +
                "OR (evaluable_user_id IS NULL AND evaluable_establishment_id IS NOT NULL)");
            // Exactly one evaluator target.
            t.HasCheckConstraint(
                "ck_evaluations_evaluator_one_of",
                "(evaluator_user_id IS NOT NULL AND evaluator_establishment_id IS NULL) " +
                "OR (evaluator_user_id IS NULL AND evaluator_establishment_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EvaluableUserId).HasMaxLength(200);
        builder.Property(x => x.EvaluatorUserId).HasMaxLength(200);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.Property(x => x.SuccessManagementCriteriaComment).HasMaxLength(500);

        // --- Relationships --------------------------------------------------
        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Offer>()
            .WithMany()
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.EvaluableEstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.EvaluatorEstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK to users.identity_id — local users are keyed by Guid Id with
        // identity_id as a partial-unique non-PK column. Evaluator/evaluable
        // user is the IdM sub claim; stored as plain string. The endpoint
        // layer validates that the sub belongs to a real user row via the
        // same Users lookup used by AddMember (spec §6.3).

        // --- Indexes --------------------------------------------------------
        builder.HasIndex(x => x.OfferId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_evaluations_offer");

        builder.HasIndex(x => x.OpportunityId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_evaluations_opportunity");

        // One evaluation per (offer, evaluator user) — prevents the worker
        // from evaluating the same offer twice.
        builder.HasIndex(x => new { x.OfferId, x.EvaluatorUserId })
            .IsUnique()
            .HasFilter("evaluator_user_id IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ux_evaluations_offer_evaluator_user");

        // One evaluation per (offer, evaluator establishment) — same idea
        // for the establishment side.
        builder.HasIndex(x => new { x.OfferId, x.EvaluatorEstablishmentId })
            .IsUnique()
            .HasFilter("evaluator_establishment_id IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ux_evaluations_offer_evaluator_establishment");
    }
}
