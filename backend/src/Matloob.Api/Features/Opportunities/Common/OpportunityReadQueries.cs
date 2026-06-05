using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Common;

/// <summary>
/// Shared LINQ loaders for the read endpoints. Centralised so every
/// browse / show / mine endpoint hydrates the same nested resources
/// (category, issuer, nationality, success criteria, uploads,
/// applicants_count) the same way.
/// </summary>
internal static class OpportunityReadQueries
{
    /// <summary>
    /// Status set treated as readable for the public browse endpoints.
    /// Matches Laravel's <c>byApplicable()</c> + <c>byApplicableEvent()</c>
    /// scopes (Drafted is internal, Finished/Ended are post-life).
    /// </summary>
    public static readonly OpportunityStatus[] BrowsableStatuses =
    [
        OpportunityStatus.Upcoming,
        OpportunityStatus.Active,
    ];

    /// <summary>
    /// Hydrates the side data a single opportunity needs for
    /// <see cref="OpportunityReadMapper.Map"/>.
    /// </summary>
    public static async Task<OpportunityReadBundle> LoadSidecarAsync(
        AppDbContext db,
        Opportunity opportunity,
        string? subClaim,
        Guid? establishmentApplicantId,
        CancellationToken ct)
    {
        var category = await db.OpportunityCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == opportunity.OpportunityCategoryId, ct);

        var issuer = await db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == opportunity.IssuerEstablishmentId, ct);

        Nationality? nationality = null;
        if (opportunity.NationalityId is { } natId)
        {
            nationality = await db.Nationalities
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == natId, ct);
        }

        var successCriteria = await db.SuccessManagementCriteria
            .AsNoTracking()
            .Where(sc => sc.OpportunityId == opportunity.Id)
            .OrderBy(sc => sc.CreatedAt)
            .ToListAsync(ct);

        var uploads = await (
            from oa in db.OpportunityAssets.AsNoTracking()
            join a in db.Assets.AsNoTracking() on oa.AssetId equals a.Id
            where oa.OpportunityId == opportunity.Id
            orderby oa.UploadedAt
            select new OpportunityUploadDto
            {
                Id = a.Id,
                FileName = a.OriginalFileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
            }).ToListAsync(ct);

        var applicantsCount = await db.OpportunityApplications
            .AsNoTracking()
            .CountAsync(app => app.OpportunityId == opportunity.Id, ct);

        // The frontend dereferences `opportunity.event.*` unguarded on every
        // opportunity card / detail / application / evaluation screen, so the
        // owning event is part of the standard sidecar — hydrate it here once
        // and every read path gets it for free (no more `event: null`).
        var eventEntity = await db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == opportunity.EventId, ct);

        bool? isApplied = null;
        if (!string.IsNullOrEmpty(subClaim))
        {
            isApplied = await db.OpportunityApplications
                .AsNoTracking()
                .AnyAsync(app =>
                    app.OpportunityId == opportunity.Id &&
                    app.ApplicantUserId == subClaim, ct);
        }
        else if (establishmentApplicantId is { } estId)
        {
            isApplied = await db.OpportunityApplications
                .AsNoTracking()
                .AnyAsync(app =>
                    app.OpportunityId == opportunity.Id &&
                    app.ApplicantEstablishmentId == estId, ct);
        }

        return new OpportunityReadBundle(
            category,
            issuer,
            nationality,
            successCriteria,
            uploads,
            applicantsCount,
            isApplied,
            eventEntity);
    }
}

internal sealed record OpportunityReadBundle(
    OpportunityCategory? Category,
    Establishment? Issuer,
    Nationality? Nationality,
    IReadOnlyList<SuccessManagementCriterion> SuccessCriteria,
    IReadOnlyList<OpportunityUploadDto> Uploads,
    int ApplicantsCount,
    bool? IsApplied,
    Matloob.Domain.Events.Event? Event);
