using FastEndpoints;
using FluentValidation.Results;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Events.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>POST /api/establishments/events</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/events</c>) — create a (draft)
/// event from the first wizard step(s). Multipart; later steps update via PATCH.
/// </summary>
public sealed class CreateEventEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public CreateEventEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Post(
            "/api/establishments/events",
            "/api/v1/establishments/{establishmentId}/events");
        Policies(MatloobPolicies.User);
        AllowFormData();
        Description(b => b
            .Produces<EventResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "Create an establishment event (multi-step draft).");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var form = await HttpContext.Request.ReadFormAsync(ct);

        var errors = await EventWriteSupport.ValidateAsync(_db, form, isCreate: true, ct);
        if (errors.Count > 0)
        {
            foreach (var (field, message) in errors)
                ValidationFailures.Add(new ValidationFailure(field, message));
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        if (EventWriteSupport.IsPrecognitive(HttpContext))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var typeId = Guid.Parse(EventWriteSupport.Field(form, "step_one", "type_uuid")!);
        var seasonId = Guid.TryParse(EventWriteSupport.Field(form, "step_one", "season_id"), out var sid)
            ? sid : (Guid?)null;
        var @event = new Event(
            Guid.NewGuid(),
            establishmentId.Value,
            typeId,
            EventWriteSupport.Field(form, "step_one", "name")!,
            EventWriteSupport.Field(form, "step_one", "description")!,
            seasonId);
        _db.Events.Add(@event);

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        await EventWriteSupport.ApplyStepsAsync(_db, @event, form, today, ct);
        await _db.SaveChangesAsync(ct);

        var response = await EventReadMapper.BuildAsync(_db, @event, ct);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/events/{@event.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}
