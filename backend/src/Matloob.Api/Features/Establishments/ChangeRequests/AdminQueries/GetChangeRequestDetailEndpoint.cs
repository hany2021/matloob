using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.AdminQueries;

/// <summary>
/// <c>GET /api/v1/admin/establishments/change-requests/{id}</c> — full diff
/// view for the admin review pane. Returns the live values + the
/// proposed values for every editable column, so the admin UI can render
/// a side-by-side diff without computing it client-side from two separate
/// fetches.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class GetChangeRequestDetailEndpoint
    : EndpointWithoutRequest<ChangeRequestDetailResponse>
{
    private readonly AppDbContext _db;

    public GetChangeRequestDetailEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/establishments/change-requests/{id}");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ChangeRequestDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Full diff view for a change request.";
            s.Description =
                "Returns the live Establishment fields alongside the " +
                "Proposed* values so the admin UI can render a diff.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var cr = await _db.EstablishmentChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cr is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == cr.EstablishmentId, ct);
        if (establishment is null)
        {
            // Orphan CR -- treat as not-found at the admin tier; the
            // background job that cleans these up isn't here yet.
            await Send.NotFoundAsync(ct);
            return;
        }

        var liveDocs = await _db.EstablishmentDocuments
            .AsNoTracking()
            .Where(d => d.EstablishmentId == cr.EstablishmentId)
            .Select(d => new ChangeRequestLiveDocument(d.DocumentType, d.AssetId))
            .ToListAsync(ct);

        var response = new ChangeRequestDetailResponse(
            Id: cr.Id,
            EstablishmentId: cr.EstablishmentId,
            Status: cr.Status,
            CreatedByUserId: cr.CreatedByUserId,
            CreatedAt: cr.CreatedAt,
            SubmittedAt: cr.SubmittedAt,
            ReviewedAt: cr.ReviewedAt,
            ReviewedByAdminId: cr.ReviewedByAdminId,
            ReviewReason: cr.ReviewReason,
            AppliedAt: cr.AppliedAt,
            Live: BuildLive(establishment, liveDocs),
            Proposed: BuildProposed(cr));

        await Send.OkAsync(response, ct);
    }

    private static ChangeRequestLiveSnapshot BuildLive(
        Establishment e,
        IReadOnlyList<ChangeRequestLiveDocument> docs) => new(
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
            Documents: docs);

    private static ChangeRequestProposedSnapshot BuildProposed(EstablishmentChangeRequest cr) => new(
        Name: cr.ProposedName,
        CommercialRegistrationNumber: cr.ProposedCommercialRegistrationNumber,
        CommercialRegistrationExpiry: cr.ProposedCommercialRegistrationExpiry,
        LaborOfficeId: cr.ProposedLaborOfficeId,
        SequenceNumber: cr.ProposedSequenceNumber,
        City: cr.ProposedCity,
        Email: cr.ProposedEmail,
        Phone: cr.ProposedPhone,
        EconomicActivity: cr.ProposedEconomicActivity,
        SubEconomicActivity: cr.ProposedSubEconomicActivity,
        District: cr.ProposedDistrict,
        Area: cr.ProposedArea,
        Street: cr.ProposedStreet,
        Description: cr.ProposedDescription,
        LocationTitle: cr.ProposedLocationTitle,
        Latitude: cr.ProposedLatitude,
        Longitude: cr.ProposedLongitude,
        BuildingNumber: cr.ProposedBuildingNumber,
        PostalCode: cr.ProposedPostalCode,
        AdditionalNumber: cr.ProposedAdditionalNumber,
        Website: cr.ProposedWebsite,
        YearsOfExperience: cr.ProposedYearsOfExperience,
        EstablishmentSize: cr.ProposedEstablishmentSize,
        AdditionalContactNumber: cr.ProposedAdditionalContactNumber,
        AuthorizationLetterAssetId: cr.ProposedAuthorizationLetterAssetId,
        CommercialRegistrationAssetId: cr.ProposedCommercialRegistrationAssetId);
}

public sealed record ChangeRequestDetailResponse(
    Guid Id,
    Guid EstablishmentId,
    EstablishmentChangeRequestStatus Status,
    string CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedByAdminId,
    string? ReviewReason,
    DateTimeOffset? AppliedAt,
    ChangeRequestLiveSnapshot Live,
    ChangeRequestProposedSnapshot Proposed);

public sealed record ChangeRequestLiveSnapshot(
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
    IReadOnlyList<ChangeRequestLiveDocument> Documents);

public sealed record ChangeRequestProposedSnapshot(
    string? Name,
    string? CommercialRegistrationNumber,
    DateOnly? CommercialRegistrationExpiry,
    string? LaborOfficeId,
    string? SequenceNumber,
    string? City,
    string? Email,
    string? Phone,
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
    Guid? AuthorizationLetterAssetId,
    Guid? CommercialRegistrationAssetId);

public sealed record ChangeRequestLiveDocument(
    EstablishmentDocumentType DocumentType,
    Guid AssetId);
