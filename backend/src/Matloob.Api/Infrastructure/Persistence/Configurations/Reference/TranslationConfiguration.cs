using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Reference;

internal sealed class TranslationConfiguration : IEntityTypeConfiguration<Translation>
{
    public void Configure(EntityTypeBuilder<Translation> builder)
    {
        builder.ToTable("translations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Locale).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Value).HasColumnType("text").IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Composite unique (key, locale) — matches legacy schema. Partial filter
        // so soft-deleted rows don't block reuse.
        builder.HasIndex(x => new { x.Key, x.Locale })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // Init-data filters by locale; index supports it.
        builder.HasIndex(x => x.Locale);
    }
}
