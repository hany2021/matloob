using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Establishments;

/// <summary>
/// EF mapping for <see cref="Establishment"/>. snake_case names come from
/// the global naming convention; this file adds lengths, enum-as-text, the
/// CR-number partial unique index, and read-side indexes.
///
/// The soft-delete query filter is applied centrally by
/// AppDbContext.OnModelCreating to every ISoftDeletable entity — it is NOT
/// duplicated here.
/// </summary>
internal sealed class EstablishmentConfiguration : IEntityTypeConfiguration<Establishment>
{
    public void Configure(EntityTypeBuilder<Establishment> builder)
    {
        builder.ToTable("establishments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // §3.1 required-at-submit fields. Per spec §3 they are populated by
        // the basic-info endpoint; the column itself is non-null only after
        // the user has filled it in. To keep the Draft insert legal, we
        // allow these to be NULL at the DB level for v1 and rely on the
        // SubmitForReview validator to gate the lifecycle transition.
        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.CommercialRegistrationNumber).HasMaxLength(50);
        builder.Property(x => x.LaborOfficeId).HasMaxLength(50);
        builder.Property(x => x.SequenceNumber).HasMaxLength(50);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.Phone).HasMaxLength(30);

        // §3.2 optional fields.
        builder.Property(x => x.EconomicActivity).HasMaxLength(200);
        builder.Property(x => x.SubEconomicActivity).HasMaxLength(200);
        builder.Property(x => x.District).HasMaxLength(200);
        builder.Property(x => x.Area).HasMaxLength(200);
        builder.Property(x => x.Street).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.LocationTitle).HasMaxLength(200);
        builder.Property(x => x.Latitude).HasColumnType("numeric(8,6)");
        builder.Property(x => x.Longitude).HasColumnType("numeric(9,6)");
        builder.Property(x => x.BuildingNumber).HasMaxLength(20);
        builder.Property(x => x.PostalCode).HasMaxLength(20);
        builder.Property(x => x.AdditionalNumber).HasMaxLength(20);
        builder.Property(x => x.Website).HasMaxLength(500);
        builder.Property(x => x.EstablishmentSize).HasMaxLength(50);
        builder.Property(x => x.AdditionalContactNumber).HasMaxLength(30);

        // Lifecycle.
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.CreatedByUserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ApprovedByAdminId).HasMaxLength(200);
        builder.Property(x => x.RejectedByAdminId).HasMaxLength(200);
        builder.Property(x => x.RejectionReason).HasMaxLength(2000);
        builder.Property(x => x.SuspendedByAdminId).HasMaxLength(200);
        builder.Property(x => x.SuspensionReason).HasMaxLength(2000);

        // Owner-side one-to-one FK to the shared, ownerless BankAccount
        // (same aggregate the user side references). Restrict so a referenced
        // account can't be deleted out from under the establishment.
        builder.HasOne<Domain.Users.BankAccount>()
            .WithOne()
            .HasForeignKey<Establishment>(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // §4 — partial unique CR-number across active lifecycle states. The
        // status column stores enum names (e.g. "Approved"), matching the
        // string-conversion above.
        builder.HasIndex(x => x.CommercialRegistrationNumber)
            .IsUnique()
            .HasFilter(
                "status IN ('PendingReview','Approved','Suspended') AND is_deleted = false")
            .HasDatabaseName("ux_establishments_cr_active");

        // Read-side indexes — common lookups.
        builder.HasIndex(x => x.Status).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.CreatedByUserId).HasFilter("is_deleted = false");
    }
}
