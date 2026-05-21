using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Establishments;

internal sealed class EstablishmentMemberConfiguration
    : IEntityTypeConfiguration<EstablishmentMember>
{
    public void Configure(EntityTypeBuilder<EstablishmentMember> builder)
    {
        builder.ToTable("establishment_members");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.UserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AddedByUserId).HasMaxLength(200);

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.AddedAt).IsRequired();

        // §6.2 — one active row per (establishment, user). Inactive rows
        // (IsActive=false) still count as active in the index because the
        // filter is on is_deleted only; "deactivation" and "removal" are
        // distinct concepts. Validators block re-add of an inactive member
        // without explicit reactivation.
        builder.HasIndex(x => new { x.EstablishmentId, x.UserId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_establishment_members_pair_active");

        // Look-ups used by the auth / membership probes.
        builder.HasIndex(x => x.EstablishmentId).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.Role).HasFilter("is_deleted = false");
    }
}
