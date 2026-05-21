using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Reference;

internal sealed class SuggestedAttendeeConfiguration : IEntityTypeConfiguration<SuggestedAttendee>
{
    public void Configure(EntityTypeBuilder<SuggestedAttendee> builder)
    {
        builder.ToTable("suggested_attendees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.IsActive).HasFilter("is_active = true");
    }
}
