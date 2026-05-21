using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Reference;

internal sealed class SuggestedLocationConfiguration : IEntityTypeConfiguration<SuggestedLocation>
{
    public void Configure(EntityTypeBuilder<SuggestedLocation> builder)
    {
        builder.ToTable("suggested_locations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        // Saudi-style precision: lat 8.6, lon 9.6 (matches legacy migration).
        builder.Property(x => x.Latitude).HasColumnType("numeric(8,6)");
        builder.Property(x => x.Longitude).HasColumnType("numeric(9,6)");
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.IsActive).HasFilter("is_active = true");
    }
}
