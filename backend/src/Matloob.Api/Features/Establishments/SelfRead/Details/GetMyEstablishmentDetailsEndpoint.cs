using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.SelfRead.Details;

/// <summary>
/// <c>GET /api/v1/establishments/{id}</c> — full establishment record for
/// the public frontend.
///
/// Authorization:
/// - creator (any status), OR
/// - active member, OR
/// - <c>matloob_admin</c>.
///
/// 404 vs 403: non-related callers receive <b>404</b>, not 403. This is
/// the same trade-off the Assets API made — telling strangers that an id
/// exists is an enumeration leak we can avoid for free here. Documented
/// in the endpoint summary so the public frontend doesn't try to
/// distinguish "no such id" from "you can't see this one."
///
/// Members + a one-line pending-change-request summary are included when
/// the caller has membership or admin role. Documents always come
/// through with their asset id (the assets endpoints handle download
/// authorization separately via the establishment-member grant from
/// Phase 8B).
/// </summary>
public sealed class GetMyEstablishmentDetailsEndpoint
    : EndpointWithoutRequest<MyEstablishmentDetailsResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMyEstablishmentDetailsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/v1/establishments/{id}");
        Description(b => b
            .Produces<MyEstablishmentDetailsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Full establishment record for the current user.";
            s.Description =
                "Visible to the creator, active members, and admins. " +
                "Non-related callers receive 404 (not 403) to avoid id-" +
                "enumeration leaks.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var sub = _currentUser.UserId;

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        var isCreator = string.Equals(establishment.CreatedByUserId, sub, StringComparison.Ordinal);
        var myMembership = await _db.EstablishmentMembers
            .AsNoTracking()
            .Where(m => m.EstablishmentId == id && m.UserId == sub && m.IsActive)
            .Select(m => new { m.Role })
            .FirstOrDefaultAsync(ct);
        var isMember = myMembership is not null;

        if (!(isAdmin || isCreator || isMember))
        {
            // 404 not 403 -- see the class doc comment.
            await Send.NotFoundAsync(ct);
            return;
        }

        var documents = await _db.EstablishmentDocuments
            .AsNoTracking()
            .Where(d => d.EstablishmentId == id)
            .OrderBy(d => d.DocumentType)
            .Select(d => new EstablishmentDocumentSummary(
                d.DocumentType,
                d.AssetId,
                d.UploadedAt))
            .ToListAsync(ct);

        // Members + pending-CR are only surfaced when the caller is in the
        // establishment's "inner circle" (member OR admin -- creator-only
        // doesn't qualify because pre-approval has no members and the
        // dashboard doesn't need the data).
        IReadOnlyList<EstablishmentMemberDetailsSummary>? members = null;
        if (isMember || isAdmin)
        {
            members = await _db.EstablishmentMembers
                .AsNoTracking()
                .Where(m => m.EstablishmentId == id && m.IsActive)
                .OrderBy(m => m.AddedAt)
                .Select(m => new EstablishmentMemberDetailsSummary(
                    m.Id,
                    m.UserId,
                    m.Role,
                    m.AddedAt))
                .ToListAsync(ct);
        }

        var pendingCr = await _db.EstablishmentChangeRequests
            .AsNoTracking()
            .Where(c => c.EstablishmentId == id &&
                (c.Status == EstablishmentChangeRequestStatus.Draft ||
                 c.Status == EstablishmentChangeRequestStatus.PendingReview))
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new PendingChangeRequestSummary(
                c.Id,
                c.Status,
                c.CreatedAt,
                c.SubmittedAt,
                c.CreatedByUserId))
            .FirstOrDefaultAsync(ct);

        var response = new MyEstablishmentDetailsResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            CreatedByUserId: establishment.CreatedByUserId,
            CreatedAt: establishment.CreatedAt,
            SubmittedAt: establishment.SubmittedAt,
            ApprovedAt: establishment.ApprovedAt,
            RejectedAt: establishment.RejectedAt,
            RejectionReason: establishment.RejectionReason,
            SuspendedAt: establishment.SuspendedAt,
            SuspensionReason: establishment.SuspensionReason,
            IsSponsor: establishment.IsSponsor,
            CanManageEvents: establishment.CanManageEvents,
            IsLegacyImport: establishment.IsLegacyImport,
            MyRole: myMembership?.Role,
            Name: establishment.Name,
            CommercialRegistrationNumber: establishment.CommercialRegistrationNumber,
            CommercialRegistrationExpiry: establishment.CommercialRegistrationExpiry,
            LaborOfficeId: establishment.LaborOfficeId,
            SequenceNumber: establishment.SequenceNumber,
            City: establishment.City,
            Email: establishment.Email,
            Phone: establishment.Phone,
            EconomicActivity: establishment.EconomicActivity,
            SubEconomicActivity: establishment.SubEconomicActivity,
            District: establishment.District,
            Area: establishment.Area,
            Street: establishment.Street,
            Description: establishment.Description,
            LocationTitle: establishment.LocationTitle,
            Latitude: establishment.Latitude,
            Longitude: establishment.Longitude,
            BuildingNumber: establishment.BuildingNumber,
            PostalCode: establishment.PostalCode,
            AdditionalNumber: establishment.AdditionalNumber,
            Website: establishment.Website,
            YearsOfExperience: establishment.YearsOfExperience,
            EstablishmentSize: establishment.EstablishmentSize,
            AdditionalContactNumber: establishment.AdditionalContactNumber,
            Documents: documents,
            Members: members,
            PendingChangeRequest: pendingCr);

        await Send.OkAsync(response, ct);
    }
}

public sealed record MyEstablishmentDetailsResponse(
    Guid Id,
    EstablishmentStatus Status,
    string CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    string? RejectionReason,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason,
    bool IsSponsor,
    bool CanManageEvents,
    bool IsLegacyImport,
    EstablishmentMemberRole? MyRole,
    string Name,
    string CommercialRegistrationNumber,
    DateOnly? CommercialRegistrationExpiry,
    string LaborOfficeId,
    string SequenceNumber,
    string City,
    string Email,
    string Phone,
    string? EconomicActivity,
    string? SubEconomicActivity,
    string? District,
    string? Area,
    string? Street,
    string? Description,
    string? LocationTitle,
    decimal? Latitude,
    decimal? Longitude,
    string? BuildingNumber,
    string? PostalCode,
    string? AdditionalNumber,
    string? Website,
    int? YearsOfExperience,
    string? EstablishmentSize,
    string? AdditionalContactNumber,
    IReadOnlyList<EstablishmentDocumentSummary> Documents,
    IReadOnlyList<EstablishmentMemberDetailsSummary>? Members,
    PendingChangeRequestSummary? PendingChangeRequest);

public sealed record EstablishmentDocumentSummary(
    EstablishmentDocumentType DocumentType,
    Guid AssetId,
    DateTimeOffset UploadedAt);

public sealed record EstablishmentMemberDetailsSummary(
    Guid Id,
    string UserId,
    EstablishmentMemberRole Role,
    DateTimeOffset AddedAt);

public sealed record PendingChangeRequestSummary(
    Guid Id,
    EstablishmentChangeRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    string CreatedByUserId);
