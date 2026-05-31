using Matloob.Domain.Assets;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class UserCertificateConfiguration : IEntityTypeConfiguration<UserCertificate>
{
    public void Configure(EntityTypeBuilder<UserCertificate> builder)
    {
        builder.ToTable("user_certificates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();
        builder.Property(x => x.IssuedBy).HasMaxLength(150);

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.CopyAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");
    }
}
