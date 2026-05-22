using Matloob.Domain.Assets;
using Matloob.Domain.Evaluations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Evaluations;

internal sealed class EvaluationAssetConfiguration
    : IEntityTypeConfiguration<EvaluationAsset>
{
    public void Configure(EntityTypeBuilder<EvaluationAsset> builder)
    {
        builder.ToTable("evaluation_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.UploadedByUserId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();

        builder.HasOne<Evaluation>()
            .WithMany()
            .HasForeignKey(x => x.EvaluationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.EvaluationId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_evaluation_assets_evaluation");
        builder.HasIndex(x => x.AssetId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_evaluation_assets_asset");
    }
}
