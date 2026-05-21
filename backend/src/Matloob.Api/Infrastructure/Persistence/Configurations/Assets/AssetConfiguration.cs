using Matloob.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Assets;

/// <summary>
/// EF configuration for <see cref="Asset"/>. Snake-case table + column names
/// are provided by the global naming convention; this file only adds
/// constraints (lengths, indexes, enum-as-text conversion).
///
/// Soft-delete query filter is applied by AppDbContext.OnModelCreating for
/// every <c>ISoftDeletable</c> entity, so it's not configured here.
/// </summary>
internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // File identifiers.
        builder.Property(x => x.OriginalFileName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();

        // Metadata about the bytes.
        builder.Property(x => x.ContentType).HasMaxLength(127).IsRequired(); // RFC 6838 cap
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();

        // Enums stored as text so a typo / removed value becomes an obvious
        // migration failure instead of a silent integer mismatch.
        builder.Property(x => x.StorageDriver)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Visibility)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        // Soft FKs (no DB-level FK yet — Users + Establishments arrive in
        // later phases).
        builder.Property(x => x.OwnerUserId).HasMaxLength(200);

        // Free-form caller metadata as PostgreSQL jsonb.
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        // Indexes — every one is "lookup by X for live (not soft-deleted)
        // rows" so they are filtered partials. Postgres uses them only when
        // the query also filters is_deleted = false; the EF soft-delete query
        // filter does exactly that.
        builder.HasIndex(x => x.Sha256).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.Purpose).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.OwnerUserId).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.OwnerEstablishmentId).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.Visibility).HasFilter("is_deleted = false");
        builder.HasIndex(x => x.ContentType).HasFilter("is_deleted = false");
    }
}
