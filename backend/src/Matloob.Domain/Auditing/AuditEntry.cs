using Matloob.Domain.Common;

namespace Matloob.Domain.Auditing;

/// <summary>
/// Append-only audit log entry. Written by the auditing interceptor on
/// significant entity mutations (created later in Phase 6+ when business
/// entities exist). NOT soft-deletable: deletion of an audit log defeats
/// its purpose; we use the column-rotation / retention policy at the DB
/// level instead.
///
/// Schema is intentionally provider-agnostic (no Postgres-specific types
/// in the domain). The EF Core configuration maps OldValues/NewValues to
/// <c>jsonb</c> in <see cref="Configurations.AuditEntryConfiguration"/>.
/// </summary>
public sealed class AuditEntry : BaseEntity<Guid>
{
    public string EntityName { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty; // "Created" | "Modified" | "Deleted"
    public string ChangedBy { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; private set; }
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public Guid? CorrelationId { get; private set; }

    // EF Core materialization constructor.
    private AuditEntry() { }

    public AuditEntry(
        string entityName,
        string entityId,
        string action,
        string changedBy,
        DateTimeOffset changedAt,
        string? oldValues,
        string? newValues,
        Guid? correlationId)
    {
        Id = Guid.NewGuid();
        EntityName = entityName;
        EntityId = entityId;
        Action = action;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
        OldValues = oldValues;
        NewValues = newValues;
        CorrelationId = correlationId;
    }
}
