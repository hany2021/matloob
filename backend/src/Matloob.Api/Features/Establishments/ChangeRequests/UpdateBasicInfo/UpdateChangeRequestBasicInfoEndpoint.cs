using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Common;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.UpdateBasicInfo;

/// <summary>
/// <c>PATCH /api/v1/establishments/{id}/change-requests/{changeRequestId}/basic-info</c>
/// — overwrite Proposed* fields on a Draft or Rejected change request.
///
/// The live <see cref="Establishment"/> is NEVER touched here. Approval
/// (a separate endpoint) is what writes proposed values back to the live
/// row. Until then the establishment continues operating with its current
/// data.
///
/// Auth: active Owner of the establishment OR matloob_admin.
/// </summary>
public sealed class UpdateChangeRequestBasicInfoEndpoint
    : Endpoint<UpdateChangeRequestBasicInfoRequest, UpdateChangeRequestBasicInfoResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateChangeRequestBasicInfoEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Patch("/api/v1/establishments/{id}/change-requests/{changeRequestId}/basic-info");
        Description(b => b
            .Produces<UpdateChangeRequestBasicInfoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Update Proposed* fields on a Draft / Rejected ChangeRequest.";
            s.Description =
                "null on the wire == no change; pass an empty string to clear an " +
                "optional field. Live Establishment data is NOT modified -- the " +
                "approve endpoint is what propagates Proposed* values back to " +
                "the live row.";
        });
    }

    public override async Task HandleAsync(UpdateChangeRequestBasicInfoRequest req, CancellationToken ct)
    {
        var establishmentId = Route<Guid>("id");
        var changeRequestId = Route<Guid>("changeRequestId");

        var cr = await _db.EstablishmentChangeRequests
            .FirstOrDefaultAsync(c =>
                c.Id == changeRequestId && c.EstablishmentId == establishmentId,
                ct);
        if (cr is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isOwner = await MembershipChecks.HasPermissionAsync(
                _db, establishmentId, _currentUser.UserId, Infrastructure.Auth.Permissions.ChangeRequests.Submit, ct);
            if (!isOwner)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }
        }

        // Suspended-parent guard -- spec §8 freezes ChangeRequest edits while
        // the parent establishment is suspended. The CR itself stays in
        // Draft/Rejected; we just refuse to write to it.
        if (await EstablishmentStatusGuards.WriteIfSuspendedAsync(_db, establishmentId, HttpContext, ct))
        {
            return;
        }

        if (!cr.IsEditableByOwner)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"ChangeRequest is in status '{cr.Status}'; edits are only allowed in Draft or Rejected.",
                ct);
            return;
        }

        cr.UpdateProposedBasicInfo(
            name: ChangeIfPresent(req.Name),
            commercialRegistrationNumber: ChangeIfPresent(req.CommercialRegistrationNumber),
            laborOfficeId: ChangeIfPresent(req.LaborOfficeId),
            sequenceNumber: ChangeIfPresent(req.SequenceNumber),
            city: ChangeIfPresent(req.City),
            email: ChangeIfPresent(req.Email),
            phone: ChangeIfPresent(req.Phone),
            commercialRegistrationExpiry: ChangeIfPresent(req.CommercialRegistrationExpiry),
            economicActivity: ChangeIfPresent(req.EconomicActivity),
            subEconomicActivity: ChangeIfPresent(req.SubEconomicActivity),
            district: ChangeIfPresent(req.District),
            area: ChangeIfPresent(req.Area),
            street: ChangeIfPresent(req.Street),
            description: ChangeIfPresent(req.Description),
            locationTitle: ChangeIfPresent(req.LocationTitle),
            latitude: ChangeIfPresent(req.Latitude),
            longitude: ChangeIfPresent(req.Longitude),
            buildingNumber: ChangeIfPresent(req.BuildingNumber),
            postalCode: ChangeIfPresent(req.PostalCode),
            additionalNumber: ChangeIfPresent(req.AdditionalNumber),
            website: ChangeIfPresent(req.Website),
            yearsOfExperience: ChangeIfPresent(req.YearsOfExperience),
            establishmentSize: ChangeIfPresent(req.EstablishmentSize),
            additionalContactNumber: ChangeIfPresent(req.AdditionalContactNumber));

        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(
            new UpdateChangeRequestBasicInfoResponse(
                Id: cr.Id,
                Status: cr.Status,
                UpdatedAt: cr.UpdatedAt),
            ct);
    }

    private static FieldChange<string?> ChangeIfPresent(string? value) =>
        value is null ? FieldChange<string?>.NoChange : FieldChange.SetTo<string?>(value);

    private static FieldChange<T?> ChangeIfPresent<T>(T? value) where T : struct =>
        value is null ? FieldChange<T?>.NoChange : FieldChange.SetTo<T?>(value);
}

public sealed record UpdateChangeRequestBasicInfoResponse(
    Guid Id,
    EstablishmentChangeRequestStatus Status,
    DateTimeOffset? UpdatedAt);
