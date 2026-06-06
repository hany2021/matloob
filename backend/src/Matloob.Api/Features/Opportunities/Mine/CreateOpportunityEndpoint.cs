using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using FluentValidation.Results;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Events.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Opportunities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// <c>POST /api/establishments/me/opportunities</c> (Laravel-compat) and
/// <c>POST /api/v1/establishments/{establishmentId}/opportunities</c>
/// (canonical) — create one or many opportunities for the resolved
/// establishment.
///
/// <para>
/// <b>Two body shapes are accepted.</b>
/// </para>
///
/// <para>
/// <b>1) Legacy Laravel bulk shape:</b>
/// </para>
/// <code>
/// {
///   "event_uuid": "...",
///   "opportunities": [
///     { "opportunity_category_uuid": "...", "name": "...", ... },
///     ...
///   ]
/// }
/// </code>
/// <para>
/// Returns an array of <see cref="OpportunityResponse"/>, matching
/// Laravel's <c>OpportunityResource::collection</c>.
/// </para>
///
/// <para>
/// <b>2) Canonical single shape (new):</b>
/// </para>
/// <code>
/// {
///   "event_id": "...",
///   "opportunity_category_id": "...",
///   "name": "...",
///   ...
/// }
/// </code>
/// <para>
/// Returns a single <see cref="OpportunityResponse"/>. Field aliases
/// <c>event_uuid</c> / <c>opportunity_category_uuid</c> are also accepted
/// on this shape for forward-compat with mixed clients.
/// </para>
///
/// <para>
/// Auto-status from <c>start_date</c>: Upcoming if future, Active if
/// today/past (Q-OPP-1 default).
/// </para>
///
/// <para>
/// Inline multipart uploads from Laravel (<c>opportunities[*].uploads[]</c>
/// and per-criterion uploads) are NOT auto-ingested here — clients link
/// assets via the separate <c>POST /me/opportunities/{id}/assets</c>
/// endpoint after creation. Old frontends that need the bulk multipart
/// behavior get a documented cutover path; the JSON bulk shape covers
/// every other field.
/// </para>
/// </summary>
public sealed class CreateOpportunityEndpoint
    : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;
    private readonly IFileStorage _storage;

    public CreateOpportunityEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider clock,
        IOutboxWriter outbox,
        IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _outbox = outbox;
        _storage = storage;
    }

    public override void Configure()
    {
        Post(
            "/api/establishments/me/opportunities",
            "/api/v1/establishments/{establishmentId}/opportunities");
        Policies(MatloobPolicies.User);
        // AllowFileUploads() intentionally NOT called: it restricts the endpoint
        // to multipart/form-data and 415s JSON. This endpoint must accept BOTH
        // the JSON shapes (canonical single + legacy bulk, + precognition pings)
        // AND the multipart submission (criteria + uploads). HandleAsync branches
        // on Content-Type and reads each accordingly.
        Description(b => b
            .Produces<OpportunityResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Create one or many opportunities for the resolved establishment.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(
            _db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var now = _clock.GetUtcNow();

        // -- multipart shape (frontend "add opportunity to existing event" page:
        //    event_uuid + nested opportunities[i][...] + per-item criteria/uploads).
        //    Mirrors the event-wizard step-4 parser, but ADDS to the event rather
        //    than replacing its opportunities. ------------------------------------
        if (HttpContext.Request.HasFormContentType)
        {
            await HandleMultipartAsync(establishmentId.Value, now, ct);
            return;
        }

        // -- JSON shapes (canonical single + legacy bulk) -------------------
        CreateOpportunityRequest req;
        try
        {
            req = await HttpContext.Request.ReadFromJsonAsync<CreateOpportunityRequest>(ct)
                  ?? new CreateOpportunityRequest();
        }
        catch (JsonException)
        {
            // A precognition validate ping (e.g. blurring اسم الفرصة) serialises
            // the whole file-form as JSON, so partially-filled numeric fields
            // arrive as empty strings that System.Text.Json can't coerce into
            // decimal?/int?. It's validate-only — acknowledge it (204) instead
            // of surfacing a spurious 400 on the user. A real (non-precognition)
            // submit with malformed JSON still gets the 400.
            if (EventWriteSupport.IsPrecognitive(HttpContext))
            {
                await Send.NoContentAsync(ct);
                return;
            }
            await ProblemWriter.WriteAsync(
                HttpContext, StatusCodes.Status400BadRequest,
                "invalid_json", "Request body is not valid JSON.", ct);
            return;
        }

        // Precognition pings (the file form validates as JSON, no files yet)
        // land here — never create on a ping.
        if (EventWriteSupport.IsPrecognitive(HttpContext))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var sharedEventId = req.ResolvedEventId();

        // -- bulk shape -----------------------------------------------------
        if (req.Opportunities is { Count: > 0 } items)
        {
            if (sharedEventId is null)
            {
                await ProblemWriter.WriteAsync(
                    HttpContext,
                    StatusCodes.Status400BadRequest,
                    "event_required",
                    "event_uuid (or event_id) is required when posting an opportunities[] array.",
                    ct);
                return;
            }

            var created = new List<Opportunity>(items.Count);
            foreach (var item in items)
            {
                var categoryId = item.ResolvedCategoryId();
                if (categoryId is null)
                {
                    await ProblemWriter.WriteAsync(
                        HttpContext,
                        StatusCodes.Status422UnprocessableEntity,
                        OpportunityErrorCodes.CategoryNotFound,
                        "opportunity_category_uuid (or opportunity_category_id) is required on each opportunity item.",
                        ct);
                    return;
                }

                var categoryExists = await _db.OpportunityCategories
                    .AsNoTracking()
                    .AnyAsync(c => c.Id == categoryId.Value, ct);
                if (!categoryExists)
                {
                    await ProblemWriter.WriteAsync(
                        HttpContext,
                        StatusCodes.Status422UnprocessableEntity,
                        OpportunityErrorCodes.CategoryNotFound,
                        "Opportunity category does not exist.",
                        ct);
                    return;
                }

                Opportunity opp;
                try
                {
                    opp = BuildOpportunity(
                        establishmentId.Value,
                        sharedEventId.Value,
                        categoryId.Value,
                        item.Name,
                        item.Description,
                        item.StartDate,
                        item.EndDate,
                        item.LocationTitle,
                        item.Lat ?? 0m,
                        item.Lon ?? 0m,
                        item.RequiredPersonnel ?? 1,
                        now,
                        item.CityId,
                        item.NationalityId,
                        item.MonthlySalary,
                        item.YearsOfExperienceRequired,
                        item.WorkingHoursType,
                        item.WorkingHoursFrom,
                        item.WorkingHoursTo,
                        item.Fees,
                        item.PhoneContactInformation,
                        item.EmailContactInformation,
                        item.EstablishmentClassification,
                        item.Gender);
                }
                catch (ArgumentException ex)
                {
                    await ProblemWriter.WriteAsync(
                        HttpContext,
                        StatusCodes.Status422UnprocessableEntity,
                        "invalid_opportunity_payload",
                        ex.Message,
                        ct);
                    return;
                }

                _db.Opportunities.Add(opp);
                _outbox.Enqueue(
                    OpportunityEventTypes.Created,
                    aggregateType: nameof(Opportunity),
                    aggregateId: opp.Id,
                    payload: new
                    {
                        id = opp.Id,
                        issuerEstablishmentId = opp.IssuerEstablishmentId,
                        eventId = opp.EventId,
                        opportunityCategoryId = opp.OpportunityCategoryId,
                        status = opp.Status.ToString(),
                        createdAt = now,
                        createdByUserId = _currentUser.UserId,
                    });
                created.Add(opp);
            }
            _outbox.Flush();
            await _db.SaveChangesAsync(ct);

            var responses = new List<OpportunityResponse>(created.Count);
            foreach (var opp in created)
            {
                responses.Add(await BuildResponseAsync(opp, establishmentId, ct));
            }
            // Bulk → array (matches Laravel OpportunityResource::collection).
            await Send.ResponseAsync(responses, StatusCodes.Status201Created, ct);
            return;
        }

        // -- single shape (canonical + legacy single object) ----------------
        if (sharedEventId is null)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "event_required",
                "event_id (or event_uuid) is required.",
                ct);
            return;
        }
        var singleCategoryId = req.ResolvedCategoryId();
        if (singleCategoryId is null)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.CategoryNotFound,
                "opportunity_category_id (or opportunity_category_uuid) is required.",
                ct);
            return;
        }

        var singleCategoryExists = await _db.OpportunityCategories
            .AsNoTracking()
            .AnyAsync(c => c.Id == singleCategoryId.Value, ct);
        if (!singleCategoryExists)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.CategoryNotFound,
                "Opportunity category does not exist.",
                ct);
            return;
        }

        Opportunity opportunity;
        try
        {
            opportunity = BuildOpportunity(
                establishmentId.Value,
                sharedEventId.Value,
                singleCategoryId.Value,
                req.Name,
                req.Description,
                req.StartDate,
                req.EndDate,
                req.LocationTitle,
                req.Lat ?? 0m,
                req.Lon ?? 0m,
                req.RequiredPersonnel ?? 1,
                now,
                req.CityId,
                req.NationalityId,
                req.MonthlySalary,
                req.YearsOfExperienceRequired,
                req.WorkingHoursType,
                req.WorkingHoursFrom,
                req.WorkingHoursTo,
                req.Fees,
                req.PhoneContactInformation,
                req.EmailContactInformation,
                req.EstablishmentClassification,
                req.Gender);
        }
        catch (ArgumentException ex)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                "invalid_opportunity_payload",
                ex.Message,
                ct);
            return;
        }

        _db.Opportunities.Add(opportunity);
        _outbox.Enqueue(
            OpportunityEventTypes.Created,
            aggregateType: nameof(Opportunity),
            aggregateId: opportunity.Id,
            payload: new
            {
                id = opportunity.Id,
                issuerEstablishmentId = opportunity.IssuerEstablishmentId,
                eventId = opportunity.EventId,
                opportunityCategoryId = opportunity.OpportunityCategoryId,
                status = opportunity.Status.ToString(),
                createdAt = now,
                createdByUserId = _currentUser.UserId,
            });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        var response = await BuildResponseAsync(opportunity, establishmentId, ct);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/opportunities/{opportunity.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }

    /// <summary>
    /// Multipart path: parse + validate the nested <c>opportunities[]</c> (with
    /// per-item criteria + uploads) the same way the event wizard does, confirm
    /// the <c>event_uuid</c> belongs to the resolved establishment, then ADD the
    /// opportunities to that event (no replace). Precognition pre-validation
    /// stops at 204.
    /// </summary>
    private async Task HandleMultipartAsync(Guid establishmentId, DateTimeOffset now, CancellationToken ct)
    {
        var form = await HttpContext.Request.ReadFormAsync(ct);

        var eventRaw = form["event_uuid"].ToString();
        if (string.IsNullOrWhiteSpace(eventRaw)) eventRaw = form["event_id"].ToString();
        Guid.TryParse(eventRaw, out var eventId);

        var items = EventOpportunitiesSupport.Parse(form);

        var errors = new List<(string, string)>();
        if (eventId == Guid.Empty)
            errors.Add(("event_uuid", "A valid event is required."));
        if (items.Count == 0)
            errors.Add(("opportunities", "At least one opportunity is required."));

        // Load the event once: its window bounds the opportunities (legacy
        // ValidOpportunityStart/EndDate) and it must belong to the resolved
        // establishment (legacy ValidEventForOpportunity).
        var ev = eventId == Guid.Empty
            ? null
            : await _db.Events.AsNoTracking()
                .Where(e => e.Id == eventId)
                .Select(e => new { e.EstablishmentId, e.StartDate, e.EndDate })
                .FirstOrDefaultAsync(ct);

        await EventOpportunitiesSupport.ValidateAsync(_db, items, errors, ct, ev?.StartDate, ev?.EndDate);

        if (eventId != Guid.Empty && (ev is null || ev.EstablishmentId != establishmentId))
            errors.Add(("event_uuid", "Event not found."));

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

        var created = await EventOpportunitiesSupport.AddAsync(
            _db, _storage, eventId, items, establishmentId, _currentUser.UserId, now, ct);

        foreach (var opp in created)
        {
            _outbox.Enqueue(
                OpportunityEventTypes.Created,
                aggregateType: nameof(Opportunity),
                aggregateId: opp.Id,
                payload: new
                {
                    id = opp.Id,
                    issuerEstablishmentId = opp.IssuerEstablishmentId,
                    eventId = opp.EventId,
                    opportunityCategoryId = opp.OpportunityCategoryId,
                    status = opp.Status.ToString(),
                    createdAt = now,
                    createdByUserId = _currentUser.UserId,
                });
        }
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        var responses = new List<OpportunityResponse>(created.Count);
        foreach (var opp in created)
            responses.Add(await BuildResponseAsync(opp, establishmentId, ct));
        await Send.ResponseAsync(responses, StatusCodes.Status201Created, ct);
    }

    private async Task<OpportunityResponse> BuildResponseAsync(
        Opportunity opportunity,
        Guid? establishmentId,
        CancellationToken ct)
    {
        var bundle = await OpportunityReadQueries.LoadSidecarAsync(
            _db, opportunity,
            subClaim: null,
            establishmentApplicantId: establishmentId,
            ct);
        return OpportunityReadMapper.Map(
            opportunity,
            bundle.Category,
            bundle.Issuer,
            bundle.Nationality,
            bundle.SuccessCriteria,
            bundle.Uploads,
            bundle.ApplicantsCount,
            bundle.IsApplied,
            bundle.Event);
    }

    private static Opportunity BuildOpportunity(
        Guid establishmentId,
        Guid eventId,
        Guid categoryId,
        string? name,
        string? description,
        DateOnly? startDate,
        DateOnly? endDate,
        string? locationTitle,
        decimal latitude,
        decimal longitude,
        int requiredPersonnel,
        DateTimeOffset now,
        Guid? cityId,
        Guid? nationalityId,
        decimal? monthlySalary,
        byte? yearsOfExperienceRequired,
        string? workingHoursTypeRaw,
        string? workingHoursFromRaw,
        string? workingHoursToRaw,
        decimal? fees,
        string? phoneContact,
        string? emailContact,
        IReadOnlyList<string>? classifications,
        IReadOnlyList<string>? genders)
    {
        return Opportunity.Create(
            id: Guid.NewGuid(),
            issuerEstablishmentId: establishmentId,
            eventId: eventId,
            opportunityCategoryId: categoryId,
            name: name ?? string.Empty,
            description: description ?? string.Empty,
            startDate: startDate ?? throw new ArgumentException("start_date is required.", nameof(startDate)),
            endDate: endDate ?? throw new ArgumentException("end_date is required.", nameof(endDate)),
            locationTitle: locationTitle ?? string.Empty,
            latitude: latitude,
            longitude: longitude,
            requiredPersonnel: requiredPersonnel,
            now: now,
            cityId: cityId,
            nationalityId: nationalityId,
            monthlySalary: monthlySalary,
            yearsOfExperienceRequired: yearsOfExperienceRequired,
            workingHoursType: WorkingHoursTypeWire.Parse(workingHoursTypeRaw),
            workingHoursFrom: ParseTime(workingHoursFromRaw),
            workingHoursTo: ParseTime(workingHoursToRaw),
            fees: fees,
            phoneContactInformation: phoneContact,
            emailContactInformation: emailContact,
            establishmentClassifications: CollapseClassifications(classifications),
            genders: CollapseGenders(genders));
    }

    private static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var v) ? v : null;

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParse(value, out var t) ? t : null;

    private static EstablishmentClassification CollapseClassifications(IReadOnlyList<string>? values)
    {
        if (values is null) return EstablishmentClassification.None;
        var result = EstablishmentClassification.None;
        foreach (var v in values)
        {
            result |= v.ToLowerInvariant() switch
            {
                "small" => EstablishmentClassification.Small,
                "medium" => EstablishmentClassification.Medium,
                "large" => EstablishmentClassification.Large,
                "freelancers" => EstablishmentClassification.Freelancers,
                _ => EstablishmentClassification.None,
            };
        }
        return result;
    }

    private static OpportunityGender CollapseGenders(IReadOnlyList<string>? values)
    {
        if (values is null) return OpportunityGender.None;
        var result = OpportunityGender.None;
        foreach (var v in values)
        {
            result |= v.ToLowerInvariant() switch
            {
                "male" => OpportunityGender.Male,
                "female" => OpportunityGender.Female,
                _ => OpportunityGender.None,
            };
        }
        return result;
    }
}

/// <summary>
/// Polymorphic request DTO that accepts BOTH the legacy Laravel bulk
/// shape (<c>event_uuid + opportunities[]</c>) AND the canonical
/// single-object shape (<c>event_id + flat fields</c>). All fields are
/// nullable; the handler decides which path to take.
/// </summary>
public sealed class CreateOpportunityRequest
{
    // -- shared event id (top-level on both shapes) --
    [JsonPropertyName("event_id")]
    public Guid? EventId { get; init; }

    /// <summary>Laravel legacy alias for <c>event_id</c>.</summary>
    [JsonPropertyName("event_uuid")]
    public Guid? EventUuid { get; init; }

    public Guid? ResolvedEventId() => EventId ?? EventUuid;

    // -- bulk shape --
    [JsonPropertyName("opportunities")]
    public IReadOnlyList<CreateOpportunityItem>? Opportunities { get; init; }

    // -- single-shape fields (all nullable so bulk requests don't trip
    //    FluentValidation; handler validates the chosen shape) --

    [JsonPropertyName("opportunity_category_id")]
    public Guid? OpportunityCategoryId { get; init; }

    /// <summary>Laravel legacy alias for <c>opportunity_category_id</c>.</summary>
    [JsonPropertyName("opportunity_category_uuid")]
    public Guid? OpportunityCategoryUuid { get; init; }

    public Guid? ResolvedCategoryId() => OpportunityCategoryId ?? OpportunityCategoryUuid;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("start_date")]
    public DateOnly? StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public DateOnly? EndDate { get; init; }

    [JsonPropertyName("location_title")]
    public string? LocationTitle { get; init; }

    [JsonPropertyName("lat")]
    public decimal? Lat { get; init; }

    [JsonPropertyName("lon")]
    public decimal? Lon { get; init; }

    [JsonPropertyName("required_personnel")]
    public int? RequiredPersonnel { get; init; }

    [JsonPropertyName("monthly_salary")]
    public decimal? MonthlySalary { get; init; }

    [JsonPropertyName("years_of_experience_required")]
    public byte? YearsOfExperienceRequired { get; init; }

    [JsonPropertyName("working_hours_type")]
    public string? WorkingHoursType { get; init; }

    [JsonPropertyName("working_hours_from")]
    public string? WorkingHoursFrom { get; init; }

    [JsonPropertyName("working_hours_to")]
    public string? WorkingHoursTo { get; init; }

    [JsonPropertyName("fees")]
    public decimal? Fees { get; init; }

    [JsonPropertyName("phone_contact_information")]
    public string? PhoneContactInformation { get; init; }

    [JsonPropertyName("email_contact_information")]
    public string? EmailContactInformation { get; init; }

    [JsonPropertyName("establishment_classification")]
    public IReadOnlyList<string>? EstablishmentClassification { get; init; }

    [JsonPropertyName("gender")]
    public IReadOnlyList<string>? Gender { get; init; }

    [JsonPropertyName("city_id")]
    public Guid? CityId { get; init; }

    [JsonPropertyName("nationality_id")]
    public Guid? NationalityId { get; init; }
}

/// <summary>
/// One item inside the legacy bulk <c>opportunities[]</c> array.
/// Mirrors the per-item field set from Laravel
/// <c>StoreOpportunityRequest</c> with both <c>_id</c> and <c>_uuid</c>
/// aliases on category.
/// </summary>
public sealed class CreateOpportunityItem
{
    [JsonPropertyName("opportunity_category_id")]
    public Guid? OpportunityCategoryId { get; init; }

    [JsonPropertyName("opportunity_category_uuid")]
    public Guid? OpportunityCategoryUuid { get; init; }

    public Guid? ResolvedCategoryId() => OpportunityCategoryId ?? OpportunityCategoryUuid;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("start_date")]
    public DateOnly? StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public DateOnly? EndDate { get; init; }

    [JsonPropertyName("location_title")]
    public string? LocationTitle { get; init; }

    [JsonPropertyName("lat")]
    public decimal? Lat { get; init; }

    [JsonPropertyName("lon")]
    public decimal? Lon { get; init; }

    [JsonPropertyName("required_personnel")]
    public int? RequiredPersonnel { get; init; }

    [JsonPropertyName("monthly_salary")]
    public decimal? MonthlySalary { get; init; }

    [JsonPropertyName("years_of_experience_required")]
    public byte? YearsOfExperienceRequired { get; init; }

    [JsonPropertyName("working_hours_type")]
    public string? WorkingHoursType { get; init; }

    [JsonPropertyName("working_hours_from")]
    public string? WorkingHoursFrom { get; init; }

    [JsonPropertyName("working_hours_to")]
    public string? WorkingHoursTo { get; init; }

    [JsonPropertyName("fees")]
    public decimal? Fees { get; init; }

    [JsonPropertyName("phone_contact_information")]
    public string? PhoneContactInformation { get; init; }

    [JsonPropertyName("email_contact_information")]
    public string? EmailContactInformation { get; init; }

    [JsonPropertyName("establishment_classification")]
    public IReadOnlyList<string>? EstablishmentClassification { get; init; }

    [JsonPropertyName("gender")]
    public IReadOnlyList<string>? Gender { get; init; }

    [JsonPropertyName("city_id")]
    public Guid? CityId { get; init; }

    [JsonPropertyName("nationality_id")]
    public Guid? NationalityId { get; init; }
}

public sealed class CreateOpportunityRequestValidator : Validator<CreateOpportunityRequest>
{
    public CreateOpportunityRequestValidator()
    {
        // Only validate top-level fields when the bulk array is NOT
        // present. The handler does per-item validation on the bulk
        // path, so FluentValidation stays out of the way there.
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120)
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(10).MaximumLength(3000)
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.LocationTitle).NotEmpty().MaximumLength(200)
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.RequiredPersonnel)
            .NotNull()
            .GreaterThanOrEqualTo(1).LessThanOrEqualTo(1_000_000)
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.Lat)
            .NotNull()
            .InclusiveBetween(-90m, 90m)
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.Lon)
            .NotNull()
            .InclusiveBetween(-180m, 180m)
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.StartDate).NotNull()
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.EndDate).NotNull()
            .When(x => x.Opportunities is null || x.Opportunities.Count == 0);
        RuleFor(x => x.MonthlySalary).GreaterThanOrEqualTo(0).When(x => x.MonthlySalary.HasValue);
        RuleFor(x => x.Fees).GreaterThanOrEqualTo(0).When(x => x.Fees.HasValue);
        RuleFor(x => x.YearsOfExperienceRequired)
            .LessThanOrEqualTo((byte)100).When(x => x.YearsOfExperienceRequired.HasValue);
        RuleFor(x => x.EmailContactInformation)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.EmailContactInformation));
    }
}
