using System.Globalization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Profile;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Profile.StoreExperience;

/// <summary>
/// <c>POST /api/establishments/me/profile/experience</c> (+ canonical) — add a
/// portfolio experience to the resolved establishment's profile (the profile
/// "Add experience" dialog). Shares the route with the PATCH years-of-experience
/// endpoint, distinguished by verb.
///
/// Mirrors Laravel <c>StoreProfileExperienceController</c> /
/// <c>EstablishmentExperienceService</c>: validates the category exists in the
/// event-type (type=event) or opportunity-category (type=opportunity) table
/// (422), then creates the experience with the fixed job title. JSON +
/// laravel-precognition (validation-only requests stop with 204). Returns the
/// full refreshed profile.
///
/// Auth: active member (or admin); writes blocked with 423 while Suspended.
/// </summary>
public sealed class StoreExperienceEndpoint
    : Endpoint<StoreExperienceRequest, DataEnvelope<EstablishmentMeProfileResponse>>
{
    // Legacy service hard-set job_title to "مشارك" (participant).
    private const string ParticipantJobTitle = "مشارك";

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public StoreExperienceEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.POST);
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
        Summary(s => s.Summary = "Add a portfolio experience to the resolved establishment.");
    }

    public override async Task HandleAsync(StoreExperienceRequest req, CancellationToken ct)
    {
        if (HttpContext.Request.Headers.ContainsKey("Precognition"))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        // Validator guaranteed these parse.
        EstablishmentExperienceTypeWire.TryParse(req.Type, out var type);
        var categoryId = Guid.Parse(req.Category!);
        var from = DateOnly.Parse(req.From!, CultureInfo.InvariantCulture);
        var to = DateOnly.Parse(req.To!, CultureInfo.InvariantCulture);

        // Category must exist in the matching reference table (Laravel 422).
        var categoryExists = type == EstablishmentExperienceType.Event
            ? await _db.EventTypes.AnyAsync(t => t.Id == categoryId, ct)
            : await _db.OpportunityCategories.AnyAsync(c => c.Id == categoryId, ct);
        if (!categoryExists)
        {
            AddError(r => r.Category, type == EstablishmentExperienceType.Event
                ? "The selected event category is invalid."
                : "The selected opportunity category is invalid.");
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        _db.EstablishmentExperiences.Add(new EstablishmentExperience(
            Guid.NewGuid(), establishmentId.Value, type, req.Name!, ParticipantJobTitle,
            categoryId, req.Description!, from, to));
        await _db.SaveChangesAsync(ct);

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == establishmentId.Value, ct);
        if (establishment is null) { await Send.NotFoundAsync(ct); return; }

        var response = await EstablishmentProfileReadMapper.BuildAsync(_db, establishment, ct);
        await Send.OkAsync(new DataEnvelope<EstablishmentMeProfileResponse>(response), ct);
    }
}
