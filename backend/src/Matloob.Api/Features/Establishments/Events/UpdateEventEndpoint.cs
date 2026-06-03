using System.Text.Json;
using FastEndpoints;
using FluentValidation.Results;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Events.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>PATCH /api/establishments/events/{id}</c> (+ canonical) — apply the next
/// wizard step(s) to a draft event and optionally publish. Multipart; accepts
/// PATCH/PUT/POST (the frontend PATCHes once an id exists).
/// </summary>
public sealed class UpdateEventEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IFileStorage _storage;

    public UpdateEventEndpoint(
        AppDbContext db, ICurrentUser currentUser, TimeProvider clock, IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _storage = storage;
    }

    public override void Configure()
    {
        Verbs(Http.PATCH, Http.PUT, Http.POST);
        Routes(
            "/api/establishments/events/{id:guid}",
            "/api/v1/establishments/{establishmentId}/events/{id:guid}");
        Policies(MatloobPolicies.User);
        // AllowFileUploads() intentionally NOT called — see CreateEventEndpoint.
        // EventFormReader serves both the JSON (file-less steps + precognition
        // pings) and multipart (steps with uploads) wizard submissions.
        Description(b => b
            .Produces<EventResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "Update / publish an establishment event (multi-step).");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var @event = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == id && e.EstablishmentId == establishmentId.Value, ct);
        if (@event is null) { await Send.NotFoundAsync(ct); return; }

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

        var errors = await EventWriteSupport.ValidateAsync(_db, form, isCreate: false, ct);
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

        await EventWriteSupport.ApplyStepsAsync(
            _db, _storage, @event, form, _currentUser.UserId, _clock.GetUtcNow(), ct);
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await EventReadMapper.BuildAsync(_db, @event, ct), ct);
    }
}
