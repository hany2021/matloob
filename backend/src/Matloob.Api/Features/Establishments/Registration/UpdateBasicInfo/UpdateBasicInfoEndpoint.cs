using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Common;
using Matloob.Domain.Establishments;
// Use MVC's ProblemDetails (the FastEndpoints alias diverges on extensions).
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Registration.UpdateBasicInfo;

/// <summary>
/// <c>PATCH /api/v1/establishments/registration/{id}/basic-info</c> —
/// applies a partial update to an editable Draft / Rejected establishment.
///
/// Auth: <see cref="MatloobPolicies.User"/> + per-row ownership
/// (CreatedByUserId must equal the JWT sub). Non-creator owners get 403,
/// not 404, so callers can tell "this id exists but you can't touch it"
/// apart from "no such id." Spec §10.
///
/// Status guard: only Draft / Rejected accept writes (§2). Any other
/// status returns 409 with code <c>cannot_edit_in_status</c>.
///
/// PATCH semantics: a <c>null</c> field on the wire means "leave this
/// column alone." To clear an optional field, send an empty string —
/// the domain method's <see cref="Establishment.UpdateBasicInfo"/>
/// normalizer treats whitespace-only input as <c>null</c> for optional
/// columns. The §3.1 required columns can't be blanked through this
/// endpoint; that's fine because the only sensible reason to blank them
/// would be to break submission, and the user can just refrain from
/// submitting instead.
/// </summary>
public sealed class UpdateBasicInfoEndpoint : Endpoint<UpdateBasicInfoRequest, UpdateBasicInfoResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateBasicInfoEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Patch("/api/v1/establishments/registration/{id}/basic-info");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<UpdateBasicInfoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Update the §3 field set on an editable establishment.";
            s.Description =
                "JSON-merge semantics: absent keys are unchanged; explicit null on " +
                "optional fields clears them. Allowed only in Status=Draft or Rejected.";
        });
    }

    public override async Task HandleAsync(UpdateBasicInfoRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!string.Equals(establishment.CreatedByUserId, _currentUser.UserId, StringComparison.Ordinal))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        if (!establishment.IsEditableByCreator)
        {
            await SendConflictAsync(
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot edit in status '{establishment.Status}'. Allowed: Draft, Rejected.",
                ct);
            return;
        }

        // null-on-the-wire == NoChange; any non-null value == SetTo.
        establishment.UpdateBasicInfo(
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
            additionalContactNumber: ChangeIfPresent(req.AdditionalContactNumber),
            canManageEvents: ChangeIfPresent(req.CanManageEvents));

        await _db.SaveChangesAsync(ct);

        var response = new UpdateBasicInfoResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            UpdatedAt: establishment.UpdatedAt);
        await Send.OkAsync(response, ct);
    }

    private static FieldChange<string?> ChangeIfPresent(string? value) =>
        value is null ? FieldChange<string?>.NoChange : FieldChange.SetTo<string?>(value);

    private static FieldChange<T?> ChangeIfPresent<T>(T? value) where T : struct =>
        value is null ? FieldChange<T?>.NoChange : FieldChange.SetTo<T?>(value);

    private async Task SendConflictAsync(string code, string detail, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = detail,
            Type = "https://httpstatuses.io/409",
        };
        problem.Extensions["code"] = code;

        // Send.ResponseAsync is generic over TResponse; ProblemDetails is not
        // assignable to UpdateBasicInfoResponse, so bypass it and write the
        // body directly. ProblemDetails serializes the same shape ASP.NET
        // Core uses for its own 4xx ProblemDetails replies.
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        HttpContext.Response.ContentType = "application/problem+json";
        await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }
}
