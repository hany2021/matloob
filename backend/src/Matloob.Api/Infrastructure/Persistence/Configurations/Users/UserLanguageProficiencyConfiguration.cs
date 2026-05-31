using Matloob.Domain.Reference;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class UserLanguageProficiencyConfiguration
    : IEntityTypeConfiguration<UserLanguageProficiency>
{
    public void Configure(EntityTypeBuilder<UserLanguageProficiency> builder)
    {
        builder.ToTable("user_languages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Level)
            .HasConversion(UserProfileConverters.Level)
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne<Language>()
            .WithMany()
            .HasForeignKey(x => x.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");

        // One active row per (user, language).
        builder.HasIndex(x => new { x.UserId, x.LanguageId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_user_languages_user_language_active");
    }
}
