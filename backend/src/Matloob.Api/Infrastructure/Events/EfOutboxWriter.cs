using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;
using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Infrastructure.Events;

/// <summary>
/// EF-backed implementation of <see cref="IOutboxWriter"/>. Stages events
/// in memory, materializes them onto <see cref="AppDbContext"/> as
/// <see cref="OutboxEvent"/> rows on <see cref="Flush"/>, and lets the
/// caller's next SaveChangesAsync commit them in the same transaction.
///
/// Correlation id resolution: pulls <c>X-Correlation-Id</c> off the
/// current request if present; otherwise falls back to
/// <c>HttpContext.TraceIdentifier</c>; otherwise null. Outside an HTTP
/// context (background services, EF design-time), correlation id is
/// always null — the caller can still inspect the row via
/// AggregateId / EventType.
/// </summary>
internal sealed class EfOutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions PayloadOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly List<Pending> _pending = new();

    public EfOutboxWriter(
        AppDbContext db,
        TimeProvider clock,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _clock = clock;
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enqueue(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentNullException.ThrowIfNull(payload);

        // Serialize at enqueue time so the payload's snapshot reflects
        // the values the endpoint had when emitting, not later mutations.
        var json = JsonSerializer.Serialize(payload, PayloadOptions);
        _pending.Add(new Pending(eventType, aggregateType, aggregateId, json));
    }

    public void Flush()
    {
        if (_pending.Count == 0) return;

        var now = _clock.GetUtcNow();
        var correlationId = ResolveCorrelationId();

        foreach (var p in _pending)
        {
            _db.OutboxEvents.Add(new OutboxEvent(
                id: Guid.NewGuid(),
                eventType: p.EventType,
                aggregateType: p.AggregateType,
                aggregateId: p.AggregateId,
                payloadJson: p.PayloadJson,
                occurredAt: now,
                correlationId: correlationId));
        }

        _pending.Clear();
    }

    private string? ResolveCorrelationId()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return null;

        if (ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var header))
        {
            var raw = header.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                // Cap to column length to avoid a DbUpdateException on the
                // happy path; a >128-char header is almost certainly client
                // mischief and we just truncate.
                return raw.Length > 128 ? raw[..128] : raw;
            }
        }

        return ctx.TraceIdentifier;
    }

    private readonly record struct Pending(
        string EventType,
        string AggregateType,
        Guid AggregateId,
        string PayloadJson);
}
