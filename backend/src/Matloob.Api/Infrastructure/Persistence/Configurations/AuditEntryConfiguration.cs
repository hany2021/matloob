using Matloob.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        // Default table name resolution would produce "audit_entries" via
        // EFCore.NamingConventions snake_case. Made explicit for clarity.
        builder.ToTable("audit_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever(); // assigned in the domain constructor

        builder.Property(x => x.EntityName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasMaxLength(64)        // accommodates uuid strings + bigint ids
            .IsRequired();

        builder.Property(x => x.Action)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ChangedBy)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ChangedAt)
            .IsRequired();

        // Postgres jsonb for the value snapshots.
        builder.Property(x => x.OldValues)
            .HasColumnType("jsonb");

        builder.Property(x => x.NewValues)
            .HasColumnType("jsonb");

        builder.Property(x => x.CorrelationId);

        // Common query paths: by entity, by actor, by time.
        builder.HasIndex(x => new { x.EntityName, x.EntityId });
        builder.HasIndex(x => x.ChangedAt);
        builder.HasIndex(x => x.CorrelationId)
            .HasFilter("correlation_id IS NOT NULL");
    }
}
