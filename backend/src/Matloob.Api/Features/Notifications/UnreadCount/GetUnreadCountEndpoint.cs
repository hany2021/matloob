using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Infrastructure.Auth;

namespace Matloob.Api.Features.Notifications.UnreadCount;

/// <summary>
/// <c>GET /api/users/notifications/unread-count</c> and
/// <c>GET /api/establishments/notifications/unread-count</c> — number of
/// unread notifications for the current principal.
///
/// STUB. The notification module has not been ported to the new API yet
/// (only the transactional outbox dispatcher exists). The public frontend
/// polls this route on load to badge the notification bell, so we return a
/// well-formed <c>{ "count": 0 }</c> rather than 404 to keep the UI working.
/// When the real notification feature lands this endpoint is replaced with a
/// query against the notifications table.
///
/// Auth: <see cref="MatloobPolicies.User"/>. Anonymous → 401.
/// </summary>
public sealed class GetUnreadCountEndpoint : EndpointWithoutRequest<UnreadCountResponse>
{
    public override void Configure()
    {
        Get(
            "/api/users/notifications/unread-count",
            "/api/establishments/notifications/unread-count");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<UnreadCountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Notifications"));
        Summary(s =>
        {
            s.Summary = "Unread notification count for the current user.";
            s.Description =
                "STUB while the notification module is unported — always 0. " +
                "Shape { count } matches the Laravel response.";
        });
    }

    public override Task HandleAsync(CancellationToken ct)
        => Send.OkAsync(new UnreadCountResponse { Count = 0 }, ct);
}

public sealed class UnreadCountResponse : IBypassEnvelope
{
    [JsonPropertyName("count")]
    public int Count { get; init; }
}
