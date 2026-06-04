using Matloob.Api.Features.Establishments.Profile;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Features.Profile.Show;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Applications;
using Matloob.Domain.Establishments;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Applications.Common;

/// <summary>
/// Builds <see cref="OpportunityApplicationResponse"/> rows from EF
/// entities. Hydrates the applier projection (user vs establishment)
/// and the applied_by row in a single follow-up query per application
/// list to keep the read path predictable.
/// </summary>
internal static class ApplicationReadMapper
{
    public const string ApplierTypeUser = "user";

    /// <summary>
    /// Legacy Laravel emitted <c>"organization"</c> for the
    /// Establishment morph alias. We keep the same string for compat.
    /// </summary>
    public const string ApplierTypeOrganization = "organization";

    public static async Task<OpportunityApplicationResponse> MapAsync(
        AppDbContext db,
        OpportunityApplication application,
        OpportunityResponse? opportunity,
        CancellationToken ct)
    {
        var (applierType, applier) = await LoadApplierAsync(db, application, ct);
        var appliedBy = await LoadAppliedByAsync(db, application, ct);
        var status = await ApplicationStatusComputer.ComputeForOneAsync(
            db, application.Id, ct);

        return new OpportunityApplicationResponse
        {
            Id = application.Id,
            ApplierType = applierType,
            Applier = applier,
            Opportunity = opportunity,
            Status = status,
            StatusLabel = null,
            CreatedAt = application.CreatedAt.ToString("yyyy-MM-dd"),
            AppliedBy = appliedBy,
        };
    }

    private static async Task<(string ApplierType, object? Applier)> LoadApplierAsync(
        AppDbContext db,
        OpportunityApplication application,
        CancellationToken ct)
    {
        if (application.ApplicantUserId is { } sub)
        {
            var u = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdentityId == sub, ct);
            // Full individual profile (UserResource), matching the
            // frontend's IndividualProfile the applicant page reads.
            object applier = u is null
                ? new ApplicationApplierDto { Id = sub }
                : await ProfileReadMapper.BuildAsync(db, u, ct);
            return (ApplierTypeUser, applier);
        }

        if (application.ApplicantEstablishmentId is { } estId)
        {
            var e = await db.Establishments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == estId, ct);
            // Full establishment profile, matching EstablishmentProfile.
            object applier = e is null
                ? new ApplicationApplierDto { Id = estId.ToString() }
                : await EstablishmentProfileReadMapper.BuildAsync(db, e, ct);
            return (ApplierTypeOrganization, applier);
        }

        // Domain CHECK guarantees one of the two is set, but keep a
        // defensive fallback so the code is exception-safe.
        return (string.Empty, new ApplicationApplierDto());
    }

    private static async Task<ApplicationAppliedByDto?> LoadAppliedByAsync(
        AppDbContext db,
        OpportunityApplication application,
        CancellationToken ct)
    {
        if (application.AppliedByUserId is null) return null;
        var u = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdentityId == application.AppliedByUserId, ct);
        return new ApplicationAppliedByDto
        {
            Id = application.AppliedByUserId,
            Name = u?.Name,
            Email = u?.Email,
        };
    }
}
