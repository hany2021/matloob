using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Offers;

internal sealed class OfferCancellationRequestConfiguration
    : IEntityTypeConfiguration<OfferCancellationRequest>
{
    public void Configure(EntityTypeBuilder<OfferCancellationRequest> builder)
    {
        builder.ToTable("offer_cancellation_requests", t =>
        {
            // Exactly one requester target.
            t.HasCheckConstraint(
                "ck_offer_cancellation_requests_requester_one_of",
                "(requested_by_user_id IS NOT NULL AND requested_by_establishment_id IS NULL) " +
                "OR (requested_by_user_id IS NULL AND requested_by_establishment_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.RequestedByUserId).HasMaxLength(200);
        builder.Property(x => x.ReviewedByUserId).HasMaxLength(200);
        builder.Property(x => x.OtherReason).HasMaxLength(500);
        builder.Property(x => x.IsApproved).HasDefaultValue(false);
        builder.Property(x => x.IsRejected).HasDefaultValue(false);

        // --- Relationships --------------------------------------------------
        builder.HasOne<Offer>()
            .WithMany()
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.RequestedByEstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OfferCancellationReason>()
            .WithMany()
            .HasForeignKey(x => x.OfferCancellationReasonId)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Indexes --------------------------------------------------------
        builder.HasIndex(x => x.OfferId)
            .HasDatabaseName("ix_offer_cancellation_requests_offer");

        // Partial unique: at most one OPEN (not-yet-approved, not-yet-rejected)
        // cancellation request per offer. Allows reopening after a reject.
        builder.HasIndex(x => x.OfferId)
            .IsUnique()
            .HasFilter("is_approved = false AND is_rejected = false")
            .HasDatabaseName("ux_offer_cancellation_requests_open_per_offer");
    }
}
