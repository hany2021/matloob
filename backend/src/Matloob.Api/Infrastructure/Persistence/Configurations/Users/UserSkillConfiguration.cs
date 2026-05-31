using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class UserSkillConfiguration : IEntityTypeConfiguration<UserSkill>
{
    public void Configure(EntityTypeBuilder<UserSkill> builder)
    {
        builder.ToTable("user_skills");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Level)
            .HasConversion(UserProfileConverters.Level)
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");
    }
}
