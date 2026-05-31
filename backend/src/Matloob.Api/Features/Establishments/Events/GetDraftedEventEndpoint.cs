using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Events.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>GET /api/establishments/events/{id}/drafted</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/events/{id}/drafted</c>) — open a
/// draft event back in the editable wizard form. Mirrors Laravel
/// <c>ShowDraftedEventController</c> + <c>DraftedEventResource</c>: a progressive
/// payload that includes <c>step_one</c>..<c>step_four</c> up to
/// <c>steps_done</c>, plus <c>opportunities</c>.
///
/// <para>
/// Step-three success criteria and the step-four nested opportunities have no
/// backing yet (see docs/SESSION-RESUME.md backlog), so those project as empty
/// arrays — the frontend rehydrates them with optional chaining.
/// </para>
///
/// Auth: active member (or admin) of the resolved establishment; 404 if the
/// event isn't theirs (same enumeration-leak policy as the show endpoint).
/// </summary>
public sealed class GetDraftedEventEndpoint : EndpointWithoutRequest<DraftedEventResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetDraftedEventEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/events/{id:guid}/drafted",
            "/api/v1/establishments/{establishmentId}/events/{id:guid}/drafted");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<DraftedEventResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "Open one of the resolved establishment's draft events in the wizard form.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var @event = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.EstablishmentId == establishmentId.Value, ct);
        if (@event is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(await BuildAsync(@event, ct), ct);
    }

    private async Task<DraftedEventResponse> BuildAsync(Event e, CancellationToken ct)
    {
        // Old resource keyed each step block on `steps_done === N` (1..4),
        // cumulatively merging steps 1..N. Outside that band (0 or the
        // published 5) no step blocks are emitted.
        var n = e.StepsDone is >= 1 and <= 4 ? e.StepsDone : 0;

        DraftedStepOne? stepOne = n >= 1
            ? new DraftedStepOne(
                e.EventTypeId, e.Name, e.Description, e.Size, e.Size, e.Classification, e.SeasonId)
            : null;

        DraftedStepTwo? stepTwo = null;
        if (n >= 2)
        {
            var uploads = await MediaSupport.ListAsync(
                _db, EventWriteSupport.EventModelType, e.Id.ToString(),
                EventWriteSupport.UploadsCollection, ct);
            stepTwo = new DraftedStepTwo(
                e.Latitude, e.Longitude,
                e.StartDate?.ToString("yyyy-MM-dd"), e.EndDate?.ToString("yyyy-MM-dd"),
                e.MinAttendees, e.MaxAttendees, e.LocationTitle, uploads);
        }

        DraftedStepThree? stepThree = n >= 3
            ? new DraftedStepThree(await SuccessCriteriaSupport.ProjectForEventAsync(_db, e.Id, ct))
            : null;

        DraftedStepFour? stepFour = null;
        if (n >= 4)
        {
            var categoryIds = await _db.EventOpportunityCategories.AsNoTracking()
                .Where(p => p.EventId == e.Id)
                .Select(p => p.OpportunityCategoryId)
                .ToListAsync(ct);
            stepFour = new DraftedStepFour(categoryIds);
        }

        return new DraftedEventResponse
        {
            Id = e.Id,
            StepOne = stepOne,
            StepTwo = stepTwo,
            StepThree = stepThree,
            StepFour = stepFour,
            StepsDone = e.StepsDone,
            Opportunities = [],
        };
    }
}

// -- Response shape (mirrors DraftedEventResource / the wizard's EventForm) ----

public sealed class DraftedEventResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("step_one")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DraftedStepOne? StepOne { get; init; }

    [JsonPropertyName("step_two")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DraftedStepTwo? StepTwo { get; init; }

    [JsonPropertyName("step_three")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DraftedStepThree? StepThree { get; init; }

    [JsonPropertyName("step_four")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DraftedStepFour? StepFour { get; init; }

    [JsonPropertyName("steps_done")]
    public int StepsDone { get; init; }

    [JsonPropertyName("opportunities")]
    public IReadOnlyList<object> Opportunities { get; init; } = [];
}

public sealed record DraftedStepOne(
    [property: JsonPropertyName("type_uuid")] Guid TypeUuid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("size")] string? Size,
    [property: JsonPropertyName("size_label")] string? SizeLabel,
    [property: JsonPropertyName("classification")] string? Classification,
    [property: JsonPropertyName("season_id")] Guid? SeasonId);

public sealed record DraftedStepTwo(
    [property: JsonPropertyName("lat")] decimal? Lat,
    [property: JsonPropertyName("lon")] decimal? Lon,
    [property: JsonPropertyName("start_date")] string? StartDate,
    [property: JsonPropertyName("end_date")] string? EndDate,
    [property: JsonPropertyName("min_attendees")] int? MinAttendees,
    [property: JsonPropertyName("max_attendees")] int? MaxAttendees,
    [property: JsonPropertyName("location_title")] string? LocationTitle,
    [property: JsonPropertyName("uploads")] IReadOnlyList<MediaDto> Uploads);

public sealed record DraftedStepThree(
    [property: JsonPropertyName("success_criteria")] IReadOnlyList<object> SuccessCriteria);

public sealed record DraftedStepFour(
    [property: JsonPropertyName("opportunities_categories")] IReadOnlyList<Guid> OpportunitiesCategories);
