namespace Matloob.Api.Infrastructure.Events;

/// <summary>
/// Application-facing entry point for emitting outbox events. Endpoints
/// don't construct <see cref="Matloob.Domain.Events.OutboxEvent"/> rows
/// directly; they go through this interface so the payload-shaping and
/// correlation-id capture logic stays in one place.
///
/// The writer is request-scoped. It tracks rows in-memory and flushes
/// them onto the DbContext's change tracker on
/// <see cref="FlushAsync"/> — call FlushAsync BEFORE
/// SaveChangesAsync so the inserts ride along in the same transaction.
/// (Or just call <see cref="EnqueueAndFlush"/> for the common one-row
/// path.)
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Stage an event for the next flush. Payload is JSON-serialized
    /// lazily; pass any DTO that serializes cleanly.
    /// </summary>
    void Enqueue(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object payload);

    /// <summary>
    /// Materialize all staged events as <see cref="Matloob.Domain.Events.OutboxEvent"/>
    /// rows on the DbContext. Idempotent: calling repeatedly with nothing
    /// staged is a no-op. The caller invokes SaveChangesAsync next.
    /// </summary>
    void Flush();
}
