using Matloob.Domain.Events;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Events;

internal sealed class EventOpportunityCategoryConfiguration
    : IEntityTypeConfiguration<EventOpportunityCategory>
{
    public void Configure(EntityTypeBuilder<EventOpportunityCategory> builder)
    {
        builder.ToTable("event_opportunity_category");
        builder.HasKey(x => new { x.EventId, x.OpportunityCategoryId });

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<OpportunityCategory>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OpportunityCategoryId)
            .HasDatabaseName("ix_event_opportunity_category_category");
    }
}
