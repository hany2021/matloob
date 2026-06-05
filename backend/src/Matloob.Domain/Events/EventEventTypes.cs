namespace Matloob.Domain.Events;

/// <summary>
/// Canonical outbox <c>event_type</c> string constants for the Event
/// aggregate. Wire these into <c>IOutboxWriter.Enqueue</c> when a
/// time-driven transition happens (status sync); downstream subscribers key
/// off the literal value — DO NOT rename without coordinating a schema
/// migration.
/// </summary>
public static class EventEventTypes
{
    public const string Started  = "event.started";
    public const string Finished = "event.finished";
}
