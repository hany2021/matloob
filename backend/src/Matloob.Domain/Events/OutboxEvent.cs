using Matloob.Domain.Common;

namespace Matloob.Domain.Events;

/// <summary>
/// Transactional outbox row. Written in the same DbContext SaveChanges as
/// the aggregate change that triggered it, so an event either fully persists
/// alongside its row or doesn't persist at all — no half-published states.
///
/// The dispatcher background service later picks up rows with
/// <see cref="ProcessedAt"/> = null, hands them to in-process handlers
/// (today: a no-op that marks them processed), and stamps the result.
///
/// Deliberately inherits <see cref="BaseEntity{TId}"/> (NOT
/// <see cref="BaseAuditableEntity{TId}"/>): outbox rows are an audit trail
/// in their own right, they aren't soft-deletable, and the audit
/// interceptor shouldn't churn UpdatedBy/UpdatedAt on every dispatcher
/// stamp — we manage <see cref="OccurredAt"/> + <see cref="ProcessedAt"/>
/// explicitly.
/// </summary>
public sealed class OutboxEvent : BaseEntity<Guid>
{
    /// <summary>
    /// Logical event name, e.g. <c>EstablishmentApproved</c>. Stable across
    /// the lifetime of the system; subscribers branch on this string.
    /// </summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>
    /// CLR-ish name of the source aggregate, e.g. <c>Establishment</c>.
    /// Pairs with <see cref="AggregateId"/> for partitioning + ordering.
    /// </summary>
    public string AggregateType { get; private set; } = string.Empty;

    public Guid AggregateId { get; private set; }

    /// <summary>JSON payload. Shape varies per <see cref="EventType"/>; stored as jsonb in Postgres.</summary>
    public string PayloadJson { get; private set; } = "{}";

    /// <summary>
    /// When the application emitted the event. Set by the writer, not by
    /// an interceptor — outbox correctness depends on the timestamp
    /// matching the originating SaveChanges.
    /// </summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Stamped by the dispatcher once handlers have run successfully.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>
    /// Last failure message if any handler threw on the most recent attempt.
    /// Cleared when a later attempt succeeds.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Counts attempted dispatches. Lets the dispatcher back off or alert
    /// once a row passes a configured threshold.
    /// </summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Correlation id propagated from the HTTP request (or future trace id)
    /// that produced the event. Lets the audit trail tie an event back to
    /// its inbound request.
    /// </summary>
    public string? CorrelationId { get; private set; }

    private OutboxEvent() { }

    public OutboxEvent(
        Guid id,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string payloadJson,
        DateTimeOffset occurredAt,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);

        Id = id;
        EventType = eventType;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        Attempts = 0;
    }

    /// <summary>
    /// Mark a successful dispatch. Idempotent at the row level: setting
    /// <see cref="ProcessedAt"/> a second time is harmless because the
    /// dispatcher's WHERE clause filters out non-null rows.
    /// </summary>
    public void MarkProcessed(DateTimeOffset at)
    {
        ProcessedAt = at;
        Error = null;
    }

    /// <summary>
    /// Mark a failed dispatch attempt. Increments <see cref="Attempts"/>
    /// and captures the error string so an operator can grep the table.
    /// </summary>
    public void MarkFailed(string error)
    {
        Attempts += 1;
        Error = error.Length > 4000 ? error[..4000] : error;
    }
}
