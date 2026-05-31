using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Infrastructure.Auth;

namespace Matloob.Api.Features.Notifications.MarkAsRead;

/// <summary>
/// <c>POST /api/users/notifications/mark-as-read</c> and
/// <c>POST /api/establishments/notifications/mark-as-read</c> — mark a single
/// notification as read.
///
/// STUB. The notification module is unported, so there is nothing to mutate
/// yet; we accept the request and return an empty <c>{ "data": null }</c>
/// envelope so the frontend's optimistic mark-as-read mutation resolves
/// successfully. Replaced with a real update when the feature lands.
///
/// Auth: <see cref="MatloobPolicies.User"/>. Anonymous → 401.
/// </summary>
public sealed class MarkAsReadEndpoint : Endpoint<MarkAsReadRequest, MarkAsReadResponse>
{
    public override void Configure()
    {
        Post(
            "/api/users/notifications/mark-as-read",
            "/api/establishments/notifications/mark-as-read");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<MarkAsReadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Notifications"));
        Summary(s =>
        {
            s.Summary = "Mark a notification as read.";
            s.Description =
                "STUB while the notification module is unported — accepts the " +
                "id and returns an empty data envelope.";
        });
    }

    public override Task HandleAsync(MarkAsReadRequest req, CancellationToken ct)
        => Send.OkAsync(new MarkAsReadResponse(), ct);
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
