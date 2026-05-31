using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Profile.UpdateExperience;

/// <summary>
/// <c>PATCH /api/establishments/me/profile/experience</c> (+ canonical
/// <c>/api/v1/establishments/me/profile/experience</c>) — set the resolved
/// establishment's <c>years_of_experience</c>.
///
/// Mirrors Laravel <c>UpdateProfileExperienceController</c> (which wrote the
/// value onto the establishment profile). JSON + laravel-precognition
/// (validation-only requests stop with 204).
///
/// Auth: active member (or admin); writes blocked with 423 while Suspended.
/// </summary>
public sealed class UpdateExperienceEndpoint
    : Endpoint<UpdateExperienceRequest, DataEnvelope<EstablishmentMeProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateExperienceEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        // PATCH only — the same route accepts POST for *storing* a portfolio
        // experience (StoreExperienceEndpoint). PATCH carries years_of_experience.
        Verbs(Http.PATCH);
        Routes(
            "/api/establishments/me/profile/experience",
            "/api/v1/establishments/me/profile/experience");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<DataEnvelope<EstablishmentMeProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Profile"));
        Summary(s => s.Summary = "Set the resolved establishment's years of experience.");
    }

    public override async Task HandleAsync(UpdateExperienceRequest req, CancellationToken ct)
    {
        if (HttpContext.Request.Headers.ContainsKey("Precognition"))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == establishmentId.Value, ct);
        if (establishment is null) { await Send.NotFoundAsync(ct); return; }

        establishment.SetYearsOfExperience(req.YearsOfExperience!.Value);
        await _db.SaveChangesAsync(ct);

        var response = await EstablishmentProfileReadMapper.BuildAsync(_db, establishment, ct);
        await Send.OkAsync(new DataEnvelope<EstablishmentMeProfileResponse>(response), ct);
    }
}
