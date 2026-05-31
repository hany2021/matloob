using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Notifications.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Notifications.UnreadCount;

/// <summary>
/// <c>GET /api/users/notifications/unread-count</c> and
/// <c>GET /api/establishments/notifications/unread-count</c> — count of unread
/// notifications for the current recipient. Bare <c>{ count }</c> (the public
/// frontend badges the bell from it).
/// </summary>
public sealed class GetUnreadCountEndpoint : EndpointWithoutRequest<UnreadCountResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetUnreadCountEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/users/notifications/unread-count",
            "/api/establishments/notifications/unread-count");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<UnreadCountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Notifications"));
        Summary(s => s.Summary = "Unread notification count for the current recipient.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var recipient = await NotificationRecipientResolver
            .ResolveAsync(_db, HttpContext, _currentUser, ct);
        if (recipient is null) return;

        var count = await _db.Notifications.AsNoTracking()
            .CountAsync(n => n.RecipientType == recipient.Value.Type
                          && n.RecipientId == recipient.Value.Id
                          && n.ReadAt == null, ct);

        await Send.OkAsync(new UnreadCountResponse { Count = count }, ct);
    }
}

public sealed class UnreadCountResponse : IBypassEnvelope
{
    [JsonPropertyName("count")]
    public int Count { get; init; }
}
