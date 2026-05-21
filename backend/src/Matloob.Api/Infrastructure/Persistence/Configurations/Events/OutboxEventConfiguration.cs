using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Events;

/// <summary>
/// EF mapping for <see cref="OutboxEvent"/>. snake_case names come from
/// the global naming convention; this file just sets lengths + indexes
/// for the dispatcher's "next batch to process" query.
/// </summary>
internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AggregateId).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.ProcessedAt);
        builder.Property(x => x.Error).HasMaxLength(4000);
        builder.Property(x => x.Attempts).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);

        // Hot path: the dispatcher pulls the next N unprocessed rows
        // ordered by OccurredAt. The partial index keeps it cheap even
        // once the processed-row backlog is large.
        builder.HasIndex(x => new { x.OccurredAt })
            .HasFilter("processed_at IS NULL")
            .HasDatabaseName("ix_outbox_events_pending_by_time");

        // Per-aggregate lookups for ops + debugging.
        builder.HasIndex(x => new { x.AggregateType, x.AggregateId })
            .HasDatabaseName("ix_outbox_events_aggregate");

        // Branch-by-event-name scans.
        builder.HasIndex(x => x.EventType)
            .HasDatabaseName("ix_outbox_events_event_type");
    }
}
