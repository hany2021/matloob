using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// <c>GET /api/establishments/me/opportunities</c> (Laravel-compat) and
/// <c>GET /api/v1/establishments/{establishmentId}/opportunities</c>
/// (canonical) — list opportunities ISSUED BY the resolved
/// establishment. Owner view (any active member can list).
///
/// <para>
/// Returns all statuses (Drafted, Upcoming, Active, Ended, Finished) —
/// unlike the browse endpoints, the owner sees everything they have
/// published. Soft-deleted rows hidden globally.
/// </para>
///
/// <para>
/// Returns the legacy <c>GroupedOpportunityResource</c> shape — grouped
/// by status into <c>data[status][status].{ status_label, status_icon,
/// card_type, data }</c> — because the public frontend
/// (<c>OrganizerOpportunities.tsx</c>) reads exactly that double-nested
/// form. A flat list rendered as index-keyed "untitled" tabs with no
/// rows. See <see cref="GroupedOpportunitiesResponse"/>.
/// </para>
/// </summary>
public sealed class ListMineOpportunitiesEndpoint
    : EndpointWithoutRequest<GroupedOpportunitiesResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListMineOpportunitiesEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/opportunities",
            "/api/v1/establishments/{establishmentId}/opportunities");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<GroupedOpportunitiesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "List opportunities issued by the resolved establishment.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper
            .ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(
                _db, establishmentId.Value, sub, ct);
            if (!isMember)
            {
                await Send.NotFoundAsync(ct);
                return;
            }
        }

        var statusFilter = HttpContext.Request.Query["status"].FirstOrDefault();
        var nameFilter = HttpContext.Request.Query["name"].FirstOrDefault();
        var categoryFilter = ParseGuid(HttpContext.Request.Query["category"].FirstOrDefault());

        var query = _db.Opportunities
            .AsNoTracking()
            .Where(o => o.IssuerEstablishmentId == establishmentId.Value);

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<Matloob.Domain.Opportunities.OpportunityStatus>(statusFilter, ignoreCase: true, out var parsed))
        {
            query = query.Where(o => o.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var n = nameFilter.Trim().ToLowerInvariant();
            query = query.Where(o => o.Name.ToLower().Contains(n));
        }
        if (categoryFilter is { } cat)
        {
            query = query.Where(o => o.OpportunityCategoryId == cat);
        }

        var opportunities = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        // Batch-load the owning events so each card can render
        // `opportunity.event.name` (the frontend dereferences it unguarded).
        var eventIds = opportunities.Select(o => o.EventId).Distinct().ToList();
        var eventsById = await _db.Events
            .AsNoTracking()
            .Where(e => eventIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        // Batch-load applications (soft-delete filtered globally) so each
        // card can render `applicants.length` — the legacy owner list
        // eager-loaded `applicants` for exactly this. Avatars are off, so a
        // minimal ref per application is enough.
        var opportunityIds = opportunities.Select(o => o.Id).ToList();
        var applicantsByOpp = (await _db.OpportunityApplications
                .AsNoTracking()
                .Where(a => opportunityIds.Contains(a.OpportunityId))
                .Select(a => new
                {
                    a.OpportunityId,
                    Ref = new OpportunityApplicantRef
                    {
                        Id = a.Id,
                        ApplicantUserId = a.ApplicantUserId,
                        ApplicantEstablishmentId = a.ApplicantEstablishmentId,
                    },
                })
                .ToListAsync(ct))
            .GroupBy(a => a.OpportunityId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<object>)g.Select(x => (object)x.Ref).ToList());

        var active = new List<OpportunityResponse>();
        var upcoming = new List<OpportunityResponse>();
        var drafted = new List<OpportunityResponse>();
        var ended = new List<OpportunityResponse>();

        foreach (var opportunity in opportunities)
        {
            var bundle = await OpportunityReadQueries.LoadSidecarAsync(
                _db, opportunity,
                subClaim: null,
                establishmentApplicantId: establishmentId,
                ct);
            eventsById.TryGetValue(opportunity.EventId, out var eventEntity);
            var applicants = applicantsByOpp.GetValueOrDefault(opportunity.Id);
            var response = OpportunityReadMapper.Map(
                opportunity,
                bundle.Category,
                bundle.Issuer,
                bundle.Nationality,
                bundle.SuccessCriteria,
                bundle.Uploads,
                bundle.ApplicantsCount,
                bundle.IsApplied,
                eventEntity,
                applicants);

            switch (opportunity.Status)
            {
                case Matloob.Domain.Opportunities.OpportunityStatus.Active:
                    active.Add(response);
                    break;
                case Matloob.Domain.Opportunities.OpportunityStatus.Upcoming:
                    upcoming.Add(response);
                    break;
                case Matloob.Domain.Opportunities.OpportunityStatus.Drafted:
                    drafted.Add(response);
                    break;
                // Finished collapses onto the manual-ended bucket (legacy
                // labelled both "finished").
                case Matloob.Domain.Opportunities.OpportunityStatus.Ended:
                case Matloob.Domain.Opportunities.OpportunityStatus.Finished:
                    ended.Add(response);
                    break;
            }
        }

        var grouped = new GroupedOpportunitiesResponse
        {
            Active = Group("active", "Active", "active", active),
            Upcoming = Group("upcoming", "Upcoming", "upcoming", upcoming),
            Drafted = Group("drafted", "Drafted", "drafted", drafted),
            Ended = Group("ended", "Finished", "finished", ended),
        };

        await Send.OkAsync(grouped, ct);
    }

    private static Dictionary<string, OpportunityGroup> Group(
        string statusKey, string label, string cardType, IReadOnlyList<OpportunityResponse> data)
        => new()
        {
            [statusKey] = new OpportunityGroup
            {
                StatusLabel = label,
                StatusIcon = string.Empty,
                CardType = cardType,
                Data = data,
            },
        };

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var g) ? g : null;
}
