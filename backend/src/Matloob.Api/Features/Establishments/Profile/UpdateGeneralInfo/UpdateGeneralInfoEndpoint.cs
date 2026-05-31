using System.Globalization;
using FastEndpoints;
using FluentValidation.Results;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Profile.UpdateGeneralInfo;

/// <summary>
/// <c>PATCH /api/establishments/me/profile/general-info</c> (+ canonical
/// <c>/api/v1/establishments/me/profile/general-info</c>) — edit the public
/// general-info section of the resolved establishment's profile.
///
/// Mirrors Laravel <c>UpdateProfileGeneralInfoController</c> /
/// <c>UpdateProfileGeneralInfoRequest</c>. The frontend submits this as
/// <c>multipart/form-data</c> via <c>postForm</c> with a spoofed
/// <c>_method=PATCH</c> and flat <c>lat</c>/<c>lon</c> fields, so this is a
/// manual-form endpoint (same shape as the event wizard endpoints). Returns the
/// full refreshed establishment profile in a <c>{ data }</c> envelope — the
/// frontend ignores the body and refetches, but staying consistent with the
/// user-profile mutators keeps the contract uniform.
///
/// Auth: active member (or admin) of the resolved establishment; writes are
/// blocked with 423 while the establishment is Suspended.
/// </summary>
public sealed class UpdateGeneralInfoEndpoint : EndpointWithoutRequest<DataEnvelope<EstablishmentMeProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateGeneralInfoEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes(
            "/api/establishments/me/profile/general-info",
            "/api/v1/establishments/me/profile/general-info");
        Policies(MatloobPolicies.User);
        AllowFileUploads();
        Description(b => b
            .Accepts<EmptyRequest>("multipart/form-data")
            .Produces<DataEnvelope<EstablishmentMeProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Profile"));
        Summary(s => s.Summary = "Edit the resolved establishment's general-info section.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var form = await HttpContext.Request.ReadFormAsync(ct);

        var description = form["description"].ToString();
        var website = form["website"].ToString();
        var buildingNumber = form["building_number"].ToString();
        var postalCode = form["postal_code"].ToString();
        var latRaw = form["lat"].ToString();
        var lonRaw = form["lon"].ToString();

        // Validation mirrors UpdateProfileGeneralInfoRequest. Field keys stay
        // snake_case so the frontend can map errors back onto the form inputs.
        decimal lat = 0, lon = 0;
        if (string.IsNullOrWhiteSpace(description))
            ValidationFailures.Add(new ValidationFailure("description", "Description is required."));
        else if (description.Length > 300)
            ValidationFailures.Add(new ValidationFailure("description", "Description may not be greater than 300 characters."));

        if (string.IsNullOrWhiteSpace(latRaw)
            || !decimal.TryParse(latRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            || lat is < -90m or > 90m)
            ValidationFailures.Add(new ValidationFailure("lat", "Latitude must be between -90 and 90."));

        if (string.IsNullOrWhiteSpace(lonRaw)
            || !decimal.TryParse(lonRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            || lon is < -180m or > 180m)
            ValidationFailures.Add(new ValidationFailure("lon", "Longitude must be between -180 and 180."));

        if (string.IsNullOrWhiteSpace(buildingNumber) || !long.TryParse(buildingNumber, out _))
            ValidationFailures.Add(new ValidationFailure("building_number", "Building number is required and must be an integer."));

        if (!string.IsNullOrEmpty(postalCode) && (postalCode.Length != 5 || !postalCode.All(char.IsDigit)))
            ValidationFailures.Add(new ValidationFailure("postal_code", "Postal code must be 5 digits."));

        if (string.IsNullOrWhiteSpace(website) || !IsValidUrl(website))
            ValidationFailures.Add(new ValidationFailure("website", "Website must be a valid URL."));

        if (ValidationFailures.Count > 0)
        {
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        // Precognition pre-validation: stop before mutating.
        if (HttpContext.Request.Headers.ContainsKey("Precognition"))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == establishmentId.Value, ct);
        if (establishment is null) { await Send.NotFoundAsync(ct); return; }

        establishment.EditGeneralInfo(
            description: description,
            latitude: lat,
            longitude: lon,
            buildingNumber: buildingNumber,
            postalCode: string.IsNullOrEmpty(postalCode) ? null : postalCode,
            website: website);
        await _db.SaveChangesAsync(ct);

        var response = await EstablishmentProfileReadMapper.BuildAsync(_db, establishment, ct);
        await Send.OkAsync(new DataEnvelope<EstablishmentMeProfileResponse>(response), ct);
    }

    private static bool IsValidUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
