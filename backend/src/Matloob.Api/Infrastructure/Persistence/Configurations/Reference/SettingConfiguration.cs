using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Reference;

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        // Value is free-form text. The init-data endpoint sniffs its type
        // on read (bool / int / decimal / json / string) — matches the
        // legacy InitDataController behavior.
        builder.Property(x => x.Value).HasColumnType("text");
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Partial unique on Key for active rows — soft-deleted / inactive
        // rows don't collide with a new entry that reuses the same key.
        builder.HasIndex(x => x.Key)
            .IsUnique()
            .HasFilter("is_active = true AND is_deleted = false");
    }
}
