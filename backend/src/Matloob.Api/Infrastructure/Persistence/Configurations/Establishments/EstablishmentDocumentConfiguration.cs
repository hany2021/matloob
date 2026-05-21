using Matloob.Domain.Assets;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Establishments;

internal sealed class EstablishmentDocumentConfiguration
    : IEntityTypeConfiguration<EstablishmentDocument>
{
    public void Configure(EntityTypeBuilder<EstablishmentDocument> builder)
    {
        builder.ToTable("establishment_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.UploadedByUserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();

        // Hard FK to the parent establishment. Cascade is intentionally NOT
        // configured here because soft-delete is the system-wide policy; a
        // cascade DELETE would bypass the SoftDeleteInterceptor.
        builder.HasIndex(x => x.EstablishmentId).HasFilter("is_deleted = false");

        // Hard FK to the underlying Asset. Restrict on physical delete so an
        // orphaned document row never points at nothing.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.AssetId).HasFilter("is_deleted = false");

        // §5 — one active document per (establishment, type).
        builder.HasIndex(x => new { x.EstablishmentId, x.DocumentType })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_establishment_documents_slot_active");
    }
}
