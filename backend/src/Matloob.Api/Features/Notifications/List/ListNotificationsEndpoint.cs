using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Infrastructure.Auth;

namespace Matloob.Api.Features.Notifications.List;

/// <summary>
/// <c>GET /api/users/notifications</c> and
/// <c>GET /api/establishments/notifications</c> — paginated notification feed.
///
/// STUB. Mirrors the Laravel pagination envelope (<c>data</c> / <c>meta</c> /
/// <c>links</c>) the frontend's <c>ApiResponseWithPagination</c> parser
/// expects, but always returns an empty page until the notification module is
/// ported. Returning the well-formed envelope (rather than 404) lets the
/// notification dropdown render an empty state instead of erroring.
///
/// Auth: <see cref="MatloobPolicies.User"/>. Anonymous → 401.
/// </summary>
public sealed class ListNotificationsEndpoint : EndpointWithoutRequest<PaginationEnvelope>
{
    public override void Configure()
    {
        Get(
            "/api/users/notifications",
            "/api/establishments/notifications");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<PaginationEnvelope>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Notifications"));
        Summary(s =>
        {
            s.Summary = "Paginated notification feed for the current user.";
            s.Description =
                "STUB while the notification module is unported — always an " +
                "empty page in the Laravel pagination envelope.";
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var page = Query<int?>("page", isRequired: false) ?? 1;
        var path = HttpContext.Request.Path.ToString();

        var response = new PaginationEnvelope
        {
            Data = Array.Empty<object>(),
            Meta = new PaginationMeta
            {
                CurrentPage = page,
                From = 0,
                LastPage = 1,
                Links = [],
                Path = path,
                PerPage = 15,
                To = 0,
                Total = 0,
            },
            Links = new PaginationLinks
            {
                First = $"{path}?page=1",
                Last = $"{path}?page=1",
                Prev = null,
                Next = null,
            },
        };

        return Send.OkAsync(response, ct);
    }
}
