using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Profile.UpdateContactInfo;

/// <summary>
/// <c>PATCH /api/establishments/me/profile/contact-info</c> (+ canonical
/// <c>/api/v1/establishments/me/profile/contact-info</c>) — edit the contact
/// section (phone / additional phone / email) of the resolved establishment.
///
/// Mirrors Laravel <c>UpdateProfileContactInfoController</c>: the three legacy
/// tables are collapsed onto the establishment row, so this writes Phone,
/// AdditionalContactNumber and Email directly. Uniqueness across establishments
/// (excluding self) is enforced as 422, matching the legacy unique rules.
/// JSON + laravel-precognition (validation-only requests stop with 204).
///
/// Auth: active member (or admin); writes blocked with 423 while Suspended.
/// </summary>
public sealed class UpdateContactInfoEndpoint
    : Endpoint<UpdateContactInfoRequest, DataEnvelope<EstablishmentMeProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateContactInfoEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes(
            "/api/establishments/me/profile/contact-info",
            "/api/v1/establishments/me/profile/contact-info");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<DataEnvelope<EstablishmentMeProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Profile"));
        Summary(s => s.Summary = "Edit the resolved establishment's contact info.");
    }

    public override async Task HandleAsync(UpdateContactInfoRequest req, CancellationToken ct)
    {
        // Precognition pre-validation: the FluentValidation validator already
        // ran; stop before resolving/mutating (matches the user-profile slice).
        if (HttpContext.Request.Headers.ContainsKey("Precognition"))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = establishmentId.Value;

        // Uniqueness across other (non-soft-deleted) establishments — 422 to
        // match the legacy Rule::unique(...)->ignore(self) rules.
        if (await _db.Establishments.AnyAsync(e => e.Id != id && e.Email == req.Email, ct))
            AddError(r => r.Email, "Email is already in use.");
        if (await _db.Establishments.AnyAsync(e => e.Id != id && e.Phone == req.ContactNumber, ct))
            AddError(r => r.ContactNumber, "Contact number is already in use.");
        if (await _db.Establishments.AnyAsync(e => e.Id != id && e.AdditionalContactNumber == req.AdditionalContactNumber, ct))
            AddError(r => r.AdditionalContactNumber, "Additional contact number is already in use.");

        if (ValidationFailures.Count > 0)
        {
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        var establishment = await _db.Establishments.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null) { await Send.NotFoundAsync(ct); return; }

        establishment.EditContactInfo(req.ContactNumber!, req.AdditionalContactNumber!, req.Email!);
        await _db.SaveChangesAsync(ct);

        var response = await EstablishmentProfileReadMapper.BuildAsync(_db, establishment, ct);
        await Send.OkAsync(new DataEnvelope<EstablishmentMeProfileResponse>(response), ct);
    }
}
