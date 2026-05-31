using Matloob.Domain.Events;

namespace Matloob.Api.Infrastructure.Events.Dispatcher;

/// <summary>
/// In-process subscriber invoked by <see cref="OutboxDispatcherService"/> for
/// each drained outbox row. Implementations branch on
/// <see cref="OutboxEvent.EventType"/> and ignore events they don't handle.
/// Entities a handler stages on the shared <c>AppDbContext</c> are persisted in
/// the dispatcher's single SaveChanges alongside the row's processed stamp.
/// </summary>
public interface IOutboxHandler
{
    Task HandleAsync(OutboxEvent evt, CancellationToken ct);
}
