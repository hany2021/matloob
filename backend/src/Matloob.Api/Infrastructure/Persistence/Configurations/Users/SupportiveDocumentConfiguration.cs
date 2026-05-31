using Matloob.Domain.Assets;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class SupportiveDocumentConfiguration : IEntityTypeConfiguration<SupportiveDocument>
{
    public void Configure(EntityTypeBuilder<SupportiveDocument> builder)
    {
        builder.ToTable("supportive_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2048);

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.FileAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");
    }
}
