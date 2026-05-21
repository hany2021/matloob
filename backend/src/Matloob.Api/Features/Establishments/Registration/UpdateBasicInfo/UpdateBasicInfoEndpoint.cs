using System.Text.Json;
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
/// Tri-state semantics: JSON keys absent from the body leave the column
/// alone; explicit <c>null</c> on an optional column clears it. This
/// endpoint passes only the keys present in the wire payload through to
/// <see cref="Establishment.UpdateBasicInfo"/>, so an absent
/// "additionalNumber" is not the same as <c>"additionalNumber": null</c>.
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

        // The presence map lets us treat "absent" differently from "explicit null".
        // FastEndpoints parses the raw form via HttpContext; we read it again to
        // recover the original key set, lower-cased.
        var presentKeys = await ReadPresentKeysAsync(ct);

        establishment.UpdateBasicInfo(
            name: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.Name), req.Name),
            commercialRegistrationNumber: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.CommercialRegistrationNumber), req.CommercialRegistrationNumber),
            laborOfficeId: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.LaborOfficeId), req.LaborOfficeId),
            sequenceNumber: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.SequenceNumber), req.SequenceNumber),
            city: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.City), req.City),
            email: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.Email), req.Email),
            phone: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.Phone), req.Phone),
            commercialRegistrationExpiry: ChangeIfPresentValue(presentKeys, nameof(UpdateBasicInfoRequest.CommercialRegistrationExpiry), req.CommercialRegistrationExpiry),
            economicActivity: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.EconomicActivity), req.EconomicActivity),
            subEconomicActivity: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.SubEconomicActivity), req.SubEconomicActivity),
            district: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.District), req.District),
            area: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.Area), req.Area),
            street: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.Street), req.Street),
            description: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.Description), req.Description),
            locationTitle: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.LocationTitle), req.LocationTitle),
            latitude: ChangeIfPresentValue(presentKeys, nameof(UpdateBasicInfoRequest.Latitude), req.Latitude),
            longitude: ChangeIfPresentValue(presentKeys, nameof(UpdateBasicInfoRequest.Longitude), req.Longitude),
            buildingNumber: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.BuildingNumber), req.BuildingNumber),
            postalCode: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.PostalCode), req.PostalCode),
            additionalNumber: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.AdditionalNumber), req.AdditionalNumber),
            website: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.Website), req.Website),
            yearsOfExperience: ChangeIfPresentValue(presentKeys, nameof(UpdateBasicInfoRequest.YearsOfExperience), req.YearsOfExperience),
            establishmentSize: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.EstablishmentSize), req.EstablishmentSize),
            additionalContactNumber: ChangeIfPresent(presentKeys, nameof(UpdateBasicInfoRequest.AdditionalContactNumber), req.AdditionalContactNumber));

        await _db.SaveChangesAsync(ct);

        var response = new UpdateBasicInfoResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            UpdatedAt: establishment.UpdatedAt);
        await Send.OkAsync(response, ct);
    }

    private static FieldChange<string?> ChangeIfPresent(HashSet<string> keys, string name, string? value) =>
        keys.Contains(name) ? FieldChange.SetTo<string?>(value) : FieldChange<string?>.NoChange;

    private static FieldChange<T> ChangeIfPresentValue<T>(HashSet<string> keys, string name, T value) =>
        keys.Contains(name) ? FieldChange.SetTo(value) : FieldChange<T>.NoChange;

    /// <summary>
    /// Read the raw JSON body a second time to recover the set of property
    /// names the client actually included. Required because the bound DTO
    /// can't tell "absent" from "explicit null" — both bind to the C# nullable's
    /// null. The body has already been bound by FastEndpoints; we rewind and
    /// reparse, which is cheap (the body is buffered for us).
    /// </summary>
    private async Task<HashSet<string>> ReadPresentKeysAsync(CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!HttpContext.Request.Body.CanSeek)
        {
            HttpContext.Request.EnableBuffering();
        }
        HttpContext.Request.Body.Position = 0;

        try
        {
            using var doc = await JsonDocument.ParseAsync(HttpContext.Request.Body, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return keys;
            }
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                keys.Add(prop.Name);
            }
        }
        catch (JsonException)
        {
            // Body wasn't JSON; FastEndpoints' binding would already have
            // produced a 400, so we won't even reach here in practice.
        }

        return keys;
    }

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
