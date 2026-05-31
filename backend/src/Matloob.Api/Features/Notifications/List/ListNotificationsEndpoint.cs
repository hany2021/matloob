using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Notifications.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Notifications.List;

/// <summary>
/// <c>GET /api/users/notifications</c> and
/// <c>GET /api/establishments/notifications</c> — paginated notification feed
/// for the current recipient (user sub / resolved establishment). Laravel
/// pagination envelope (<c>data</c> / <c>meta</c> / <c>links</c>);
/// <c>?page</c>, <c>?per_page</c>, <c>?only_unread</c> supported.
/// </summary>
public sealed class ListNotificationsEndpoint : EndpointWithoutRequest<PaginationEnvelope>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListNotificationsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/users/notifications",
            "/api/establishments/notifications");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<PaginationEnvelope>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Notifications"));
        Summary(s => s.Summary = "Paginated notification feed for the current recipient.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var recipient = await NotificationRecipientResolver
            .ResolveAsync(_db, HttpContext, _currentUser, ct);
        if (recipient is null) return;

        var page = Math.Max(1, Query<int?>("page", isRequired: false) ?? 1);
        var perPage = Math.Clamp(Query<int?>("per_page", isRequired: false) ?? 15, 1, 100);
        var onlyUnread = Query<bool?>("only_unread", isRequired: false) ?? false;
        var path = HttpContext.Request.Path.ToString();

        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientType == recipient.Value.Type
                     && n.RecipientId == recipient.Value.Id);
        if (onlyUnread)
            query = query.Where(n => n.ReadAt == null);

        var total = await query.CountAsync(ct);
        var lastPage = total == 0 ? 1 : (int)Math.Ceiling(total / (double)perPage);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(n => new NotificationDto(
                n.Id, n.Title, n.Message, null, n.ResourceId, n.ResourceType, n.Image,
                n.Type, n.CreatedAt.ToString("o"), n.ReadAt != null))
            .ToListAsync(ct);

        var from = total == 0 ? 0 : ((page - 1) * perPage) + 1;
        var to = total == 0 ? 0 : from + items.Count - 1;

        var response = new PaginationEnvelope
        {
            Data = items,
            Meta = new PaginationMeta
            {
                CurrentPage = page,
                From = from,
                LastPage = lastPage,
                Links = [],
                Path = path,
                PerPage = perPage,
                To = to,
                Total = total,
            },
            Links = new PaginationLinks
            {
                First = $"{path}?page=1",
                Last = $"{path}?page={lastPage}",
                Prev = page > 1 ? $"{path}?page={page - 1}" : null,
                Next = page < lastPage ? $"{path}?page={page + 1}" : null,
            },
        };

        await Send.OkAsync(response, ct);
    }
}
