using System.Text.Json;
using FastEndpoints;
using FluentValidation.Results;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Events.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;

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
    private readonly IFileStorage _storage;

    public CreateEventEndpoint(
        AppDbContext db, ICurrentUser currentUser, TimeProvider clock, IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _storage = storage;
    }

    public override void Configure()
    {
        Post(
            "/api/establishments/events",
            "/api/v1/establishments/{establishmentId}/events");
        Policies(MatloobPolicies.User);
        // AllowFileUploads() intentionally NOT called: it restricts the endpoint
        // to multipart/form-data and 415s JSON. The wizard's laravel-precognition
        // form posts file-less steps (step one) + validation pings as JSON, and
        // only switches to multipart when a step actually carries files.
        // EventFormReader reads both shapes into one IFormCollection.
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
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, Infrastructure.Auth.Permissions.Events.Manage, ct);
        if (establishmentId is null) return;

        IFormCollection form;
        try
        {
            form = await EventFormReader.ReadAsync(HttpContext, ct);
        }
        catch (JsonException)
        {
            await ProblemWriter.WriteAsync(
                HttpContext, StatusCodes.Status400BadRequest,
                "invalid_json", "Request body is not valid JSON.", ct);
            return;
        }

        var errors = await EventWriteSupport.ValidateAsync(_db, form, isCreate: true, ct);
        errors = EventWriteSupport.FilterToValidateOnly(errors, HttpContext);
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

        // Upsert. The wizard always POSTs to this route (it never PATCHes) and
        // echoes the accumulated draft's id in the body from step two onward,
        // expecting an update — exactly the legacy EventService behaviour
        // (Event::whereUuid(id)->first() then update-or-create). Without this,
        // every step spawned a duplicate draft. Scope the lookup to the caller's
        // establishment so a foreign id can't be hijacked; an unknown/foreign id
        // falls through to create-new (matching legacy's null -> create).
        Event @event;
        bool created;
        var bodyId = EventWriteSupport.EventId(form);
        var existing = bodyId is { } gid
            ? await _db.Events.FirstOrDefaultAsync(
                e => e.Id == gid && e.EstablishmentId == establishmentId.Value, ct)
            : null;

        if (existing is not null)
        {
            @event = existing;
            created = false;
        }
        else
        {
            var typeId = Guid.Parse(EventWriteSupport.Field(form, "step_one", "type_uuid")!);
            var seasonId = Guid.TryParse(EventWriteSupport.Field(form, "step_one", "season_id"), out var sid)
                ? sid : (Guid?)null;
            @event = new Event(
                Guid.NewGuid(),
                establishmentId.Value,
                typeId,
                EventWriteSupport.Field(form, "step_one", "name")!,
                EventWriteSupport.Field(form, "step_one", "description")!,
                seasonId);
            _db.Events.Add(@event);
            created = true;
        }

        await EventWriteSupport.ApplyStepsAsync(
            _db, _storage, @event, form, _currentUser.UserId, _clock.GetUtcNow(), ct);
        await _db.SaveChangesAsync(ct);

        var response = await EventReadMapper.BuildAsync(_db, @event, ct);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/events/{@event.Id}";
        await Send.ResponseAsync(
            response,
            created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            ct);
    }
}
