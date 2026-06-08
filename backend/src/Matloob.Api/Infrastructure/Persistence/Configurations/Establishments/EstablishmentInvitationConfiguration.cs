using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Establishments;

internal sealed class EstablishmentInvitationConfiguration
    : IEntityTypeConfiguration<EstablishmentInvitation>
{
    public void Configure(EntityTypeBuilder<EstablishmentInvitation> builder)
    {
        builder.ToTable("establishment_invitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Email).HasMaxLength(254).IsRequired();

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.InvitedByUserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AcceptedByUserId).HasMaxLength(200);

        builder.Property(x => x.InvitedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();

        // Accept-time token lookup.
        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_invitations_token_hash");

        // At most one pending invite per (establishment, email).
        builder.HasIndex(x => new { x.EstablishmentId, x.Email })
            .IsUnique()
            .HasFilter("is_deleted = false AND status = 'Pending'")
            .HasDatabaseName("ux_invitations_one_pending_per_email_per_est");

        // Deferred-materialization sweep: accepted-but-not-yet-materialized.
        builder.HasIndex(x => x.Email)
            .HasFilter("is_deleted = false AND status = 'Accepted' AND materialized_member_id IS NULL")
            .HasDatabaseName("ix_invitations_accepted_unmaterialized");

        // List endpoint.
        builder.HasIndex(x => x.EstablishmentId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_invitations_establishment");
    }
}
