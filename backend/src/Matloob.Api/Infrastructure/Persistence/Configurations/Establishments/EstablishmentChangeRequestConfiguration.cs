using Matloob.Domain.Assets;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Establishments;

internal sealed class EstablishmentChangeRequestConfiguration
    : IEntityTypeConfiguration<EstablishmentChangeRequest>
{
    public void Configure(EntityTypeBuilder<EstablishmentChangeRequest> builder)
    {
        builder.ToTable("establishment_change_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ReviewedByAdminId).HasMaxLength(200);
        builder.Property(x => x.ReviewReason).HasMaxLength(2000);

        // Proposed §3.1 mirror — same lengths as Establishment.
        builder.Property(x => x.ProposedName).HasMaxLength(255);
        builder.Property(x => x.ProposedCommercialRegistrationNumber).HasMaxLength(50);
        builder.Property(x => x.ProposedLaborOfficeId).HasMaxLength(50);
        builder.Property(x => x.ProposedSequenceNumber).HasMaxLength(50);
        builder.Property(x => x.ProposedCity).HasMaxLength(100);
        builder.Property(x => x.ProposedEmail).HasMaxLength(320);
        builder.Property(x => x.ProposedPhone).HasMaxLength(30);

        // Proposed §3.2 mirror.
        builder.Property(x => x.ProposedEconomicActivity).HasMaxLength(200);
        builder.Property(x => x.ProposedSubEconomicActivity).HasMaxLength(200);
        builder.Property(x => x.ProposedDistrict).HasMaxLength(200);
        builder.Property(x => x.ProposedArea).HasMaxLength(200);
        builder.Property(x => x.ProposedStreet).HasMaxLength(200);
        builder.Property(x => x.ProposedDescription).HasMaxLength(2000);
        builder.Property(x => x.ProposedLocationTitle).HasMaxLength(200);
        builder.Property(x => x.ProposedLatitude).HasColumnType("numeric(8,6)");
        builder.Property(x => x.ProposedLongitude).HasColumnType("numeric(9,6)");
        builder.Property(x => x.ProposedBuildingNumber).HasMaxLength(20);
        builder.Property(x => x.ProposedPostalCode).HasMaxLength(20);
        builder.Property(x => x.ProposedAdditionalNumber).HasMaxLength(20);
        builder.Property(x => x.ProposedWebsite).HasMaxLength(500);
        builder.Property(x => x.ProposedEstablishmentSize).HasMaxLength(50);
        builder.Property(x => x.ProposedAdditionalContactNumber).HasMaxLength(30);

        // Proposed document re-uploads point at Asset rows. Restrict on
        // delete so a referenced asset cannot be hard-removed while the
        // change request still references it; the asset can be soft-deleted
        // independently after the change request is rejected.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.ProposedAuthorizationLetterAssetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.ProposedCommercialRegistrationAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.EstablishmentId).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.CreatedByUserId).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.Status).HasFilter("is_deleted = false");

        // §7.1 — exactly one PendingReview change request per establishment.
        builder.HasIndex(x => x.EstablishmentId)
            .IsUnique()
            .HasFilter("status = 'PendingReview' AND is_deleted = false")
            .HasDatabaseName("ux_establishment_change_requests_pending_per_estab");
    }
}
