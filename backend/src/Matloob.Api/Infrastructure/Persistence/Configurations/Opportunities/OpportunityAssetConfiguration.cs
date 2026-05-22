using Matloob.Domain.Assets;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Opportunities;

internal sealed class OpportunityAssetConfiguration
    : IEntityTypeConfiguration<OpportunityAsset>
{
    public void Configure(EntityTypeBuilder<OpportunityAsset> builder)
    {
        builder.ToTable("opportunity_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.UploadedByUserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();

        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OpportunityId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_opportunity_assets_opportunity");

        builder.HasIndex(x => x.AssetId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_opportunity_assets_asset");
    }
}
