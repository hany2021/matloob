using Matloob.Domain.Assets;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class UserEducationConfiguration : IEntityTypeConfiguration<UserEducation>
{
    public void Configure(EntityTypeBuilder<UserEducation> builder)
    {
        builder.ToTable("user_education");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Degree)
            .HasConversion(UserProfileConverters.Degree)
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Specialization).HasMaxLength(60);
        builder.Property(x => x.GpaSystem).IsRequired();
        builder.Property(x => x.Gpa).HasColumnType("numeric(6,2)");
        builder.Property(x => x.GraduationYear).IsRequired();

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(x => x.CopyAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");
    }
}
