using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class UserExperienceConfiguration : IEntityTypeConfiguration<UserExperience>
{
    public void Configure(EntityTypeBuilder<UserExperience> builder)
    {
        builder.ToTable("user_experiences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Company).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Position).HasMaxLength(60).IsRequired();
        builder.Property(x => x.From).IsRequired();
        builder.Property(x => x.Current).HasDefaultValue(false);
        builder.Property(x => x.Description).HasMaxLength(5000);
        builder.Property(x => x.Type)
            .HasConversion(UserProfileConverters.ExperienceType)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");
    }
}
