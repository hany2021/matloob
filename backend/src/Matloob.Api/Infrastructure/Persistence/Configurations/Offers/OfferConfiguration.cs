using Matloob.Domain.Applications;
using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Offers;

internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // String columns.
        builder.Property(x => x.AppliedByUserId).HasMaxLength(200);
        builder.Property(x => x.SentByUserId).HasMaxLength(200);
        builder.Property(x => x.LaborerCommitments).HasMaxLength(2000);
        builder.Property(x => x.OtherDetails).HasMaxLength(500);
        builder.Property(x => x.OtherRejectionReason).HasMaxLength(500);

        // Monetary.
        builder.Property(x => x.MonthlySalary).HasColumnType("numeric(10,2)");

        // Enum-as-string for both Status and Currency. Currency is a single
        // value today (SAR) but we keep the textual storage so a future
        // multi-currency rollout doesn't need a schema migration.
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(48)
            .IsRequired();
        builder.Property(x => x.Currency)
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        // --- Relationships --------------------------------------------------
        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.SenderEstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OpportunityApplication>()
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sponsor establishment — nullable FK; ON DELETE SET NULL.
        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.SponsorEstablishmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<JobTitle>()
            .WithMany()
            .HasForeignKey(x => x.JobTitleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<OpportunityCategory>()
            .WithMany()
            .HasForeignKey(x => x.JobTitleCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<OfferRejectionReason>()
            .WithMany()
            .HasForeignKey(x => x.OfferRejectionReasonId)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Indexes --------------------------------------------------------
        builder.HasIndex(x => x.ApplicationId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_offers_application");

        builder.HasIndex(x => new { x.SenderEstablishmentId, x.Status })
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_offers_sender_status");

        builder.HasIndex(x => new { x.SponsorEstablishmentId, x.Status })
            .HasFilter("sponsor_establishment_id IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ix_offers_sponsor_status");

        builder.HasIndex(x => x.Status)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_offers_status");

        // For "active offer" filtering on read — Q-OFFER-EXPIRY default
        // (compute-on-read). Indexes the validity-to column so the WHERE
        // offer_validity_to > now() filter is fast.
        builder.HasIndex(x => x.OfferValidityTo)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_offers_validity_to");
    }
}
