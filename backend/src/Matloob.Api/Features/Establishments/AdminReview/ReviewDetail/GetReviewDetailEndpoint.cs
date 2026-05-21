using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.AdminReview.ReviewDetail;

/// <summary>
/// <c>GET /api/v1/admin/establishments/{id}/review</c> — full record for the
/// admin review pane. Returns every entered field plus the document slots
/// with their asset GUIDs (download URLs are computed by the admin UI from
/// the GUIDs; we don't embed signed URLs here).
///
/// Auth: <see cref="MatloobPolicies.Admin"/>. No status filter — admins can
/// pull the record at any status (e.g. to inspect an already-approved row).
/// </summary>
public sealed class GetReviewDetailEndpoint : EndpointWithoutRequest<ReviewDetailResponse>
{
    private readonly AppDbContext _db;

    public GetReviewDetailEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/establishments/{id}/review");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ReviewDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Full establishment record for the admin review pane.";
            s.Description =
                "Includes documents with Asset GUIDs. Soft-deleted rows return 404.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var documents = await _db.EstablishmentDocuments
            .AsNoTracking()
            .Where(d => d.EstablishmentId == id)
            .Select(d => new ReviewDocument(
                d.Id,
                d.DocumentType,
                d.AssetId,
                d.UploadedByUserId,
                d.UploadedAt))
            .ToListAsync(ct);

        await Send.OkAsync(MapToResponse(establishment, documents), ct);
    }

    private static ReviewDetailResponse MapToResponse(
        Establishment e,
        IReadOnlyList<ReviewDocument> documents) => new(
            Id: e.Id,
            Status: e.Status,
            CreatedByUserId: e.CreatedByUserId,
            CreatedAt: e.CreatedAt,
            SubmittedAt: e.SubmittedAt,
            ApprovedAt: e.ApprovedAt,
            ApprovedByAdminId: e.ApprovedByAdminId,
            RejectedAt: e.RejectedAt,
            RejectedByAdminId: e.RejectedByAdminId,
            RejectionReason: e.RejectionReason,
            SuspendedAt: e.SuspendedAt,
            SuspendedByAdminId: e.SuspendedByAdminId,
            SuspensionReason: e.SuspensionReason,
            IsSponsor: e.IsSponsor,
            CanManageEvents: e.CanManageEvents,
            IsLegacyImport: e.IsLegacyImport,
            Name: e.Name,
            CommercialRegistrationNumber: e.CommercialRegistrationNumber,
            CommercialRegistrationExpiry: e.CommercialRegistrationExpiry,
            LaborOfficeId: e.LaborOfficeId,
            SequenceNumber: e.SequenceNumber,
            City: e.City,
            Email: e.Email,
            Phone: e.Phone,
            EconomicActivity: e.EconomicActivity,
            SubEconomicActivity: e.SubEconomicActivity,
            District: e.District,
            Area: e.Area,
            Street: e.Street,
            Description: e.Description,
            LocationTitle: e.LocationTitle,
            Latitude: e.Latitude,
            Longitude: e.Longitude,
            BuildingNumber: e.BuildingNumber,
            PostalCode: e.PostalCode,
            AdditionalNumber: e.AdditionalNumber,
            Website: e.Website,
            YearsOfExperience: e.YearsOfExperience,
            EstablishmentSize: e.EstablishmentSize,
            AdditionalContactNumber: e.AdditionalContactNumber,
            Documents: documents);
}

public sealed record ReviewDetailResponse(
    Guid Id,
    EstablishmentStatus Status,
    string CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedByAdminId,
    DateTimeOffset? RejectedAt,
    string? RejectedByAdminId,
    string? RejectionReason,
    DateTimeOffset? SuspendedAt,
    string? SuspendedByAdminId,
    string? SuspensionReason,
    bool IsSponsor,
    bool CanManageEvents,
    bool IsLegacyImport,
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
    IReadOnlyList<ReviewDocument> Documents);

public sealed record ReviewDocument(
    Guid Id,
    EstablishmentDocumentType DocumentType,
    Guid AssetId,
    string UploadedByUserId,
    DateTimeOffset UploadedAt);
