using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Establishments;

/// <summary>
/// Mapping for the append-only review-history table. This entity inherits
/// <see cref="Matloob.Domain.Common.BaseEntity{TId}"/>, not
/// <see cref="Matloob.Domain.Common.BaseAuditableEntity{TId}"/> — so the
/// AppDbContext's global soft-delete query filter does NOT apply, and the
/// AuditingInterceptor never touches it. The writer sets <c>OccurredAt</c>
/// at insert time.
/// </summary>
internal sealed class EstablishmentReviewHistoryConfiguration
    : IEntityTypeConfiguration<EstablishmentReviewHistory>
{
    public void Configure(EntityTypeBuilder<EstablishmentReviewHistory> builder)
    {
        builder.ToTable("establishment_review_history");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Action)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ActorUserId).HasMaxLength(200);
        builder.Property(x => x.ActorAdminId).HasMaxLength(200);
        builder.Property(x => x.Reason).HasMaxLength(2000);
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        builder.Property(x => x.OccurredAt).IsRequired();

        // Append-only: the admin UI's audit pane will sort by OccurredAt;
        // give it a composite index keyed on the establishment so common
        // queries hit a covering index.
        builder.HasIndex(x => new { x.EstablishmentId, x.OccurredAt })
            .HasDatabaseName("ix_establishment_review_history_estab_time");

        // Cross-cutting lookups.
        builder.HasIndex(x => x.ChangeRequestId);
        builder.HasIndex(x => x.Action);
    }
}
