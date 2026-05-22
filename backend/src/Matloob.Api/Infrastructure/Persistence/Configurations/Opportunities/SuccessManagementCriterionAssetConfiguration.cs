using Matloob.Domain.Assets;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Opportunities;

internal sealed class SuccessManagementCriterionAssetConfiguration
    : IEntityTypeConfiguration<SuccessManagementCriterionAsset>
{
    public void Configure(EntityTypeBuilder<SuccessManagementCriterionAsset> builder)
    {
        builder.ToTable("success_management_criterion_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.UploadedByUserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();

        builder.HasOne<SuccessManagementCriterion>()
            .WithMany()
            .HasForeignKey(x => x.SuccessManagementCriterionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SuccessManagementCriterionId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_smc_assets_criterion");
        builder.HasIndex(x => x.AssetId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_smc_assets_asset");
    }
}
