using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Notifications.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Notifications.MarkAsRead;

/// <summary>
/// <c>POST /api/users/notifications/mark-as-read</c> and
/// <c>POST /api/establishments/notifications/mark-as-read</c> — mark one
/// notification (when <c>id</c> is supplied) or all of the recipient's unread
/// notifications as read. Returns an empty <c>{ data: null }</c> envelope.
/// </summary>
public sealed class MarkAsReadEndpoint : Endpoint<MarkAsReadRequest, MarkAsReadResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public MarkAsReadEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Post(
            "/api/users/notifications/mark-as-read",
            "/api/establishments/notifications/mark-as-read");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<MarkAsReadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Notifications"));
        Summary(s => s.Summary = "Mark one or all notifications as read.");
    }

    public override async Task HandleAsync(MarkAsReadRequest req, CancellationToken ct)
    {
        var recipient = await NotificationRecipientResolver
            .ResolveAsync(_db, HttpContext, _currentUser, ct);
        if (recipient is null) return;

        var query = _db.Notifications
            .Where(n => n.RecipientType == recipient.Value.Type
                     && n.RecipientId == recipient.Value.Id
                     && n.ReadAt == null);

        if (Guid.TryParse(req.Id, out var id))
            query = query.Where(n => n.Id == id);

        var now = _clock.GetUtcNow();
        var unread = await query.ToListAsync(ct);
        foreach (var n in unread)
            n.MarkRead(now);

        if (unread.Count > 0)
            await _db.SaveChangesAsync(ct);

        await Send.OkAsync(new MarkAsReadResponse(), ct);
    }
}

public sealed class MarkAsReadRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

public sealed class MarkAsReadResponse : IBypassEnvelope
{
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}
