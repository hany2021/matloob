using Matloob.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Assets;

internal sealed class MediaConfiguration : IEntityTypeConfiguration<Media>
{
    public void Configure(EntityTypeBuilder<Media> builder)
    {
        builder.ToTable("media");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ModelType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CollectionName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UploadedByUserId).HasMaxLength(200);

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Feed query: an owner's files in a collection, in order.
        builder.HasIndex(x => new { x.ModelType, x.ModelId, x.CollectionName, x.OrderColumn })
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_media_owner_collection");

        builder.HasIndex(x => x.AssetId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_media_asset");
    }
}
