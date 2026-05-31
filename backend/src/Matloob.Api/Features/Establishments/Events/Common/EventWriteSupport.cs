using System.Globalization;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Matloob.Domain.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events.Common;

/// <summary>
/// Parses + validates + applies the public frontend's multi-step event wizard
/// payload (multipart with nested keys like <c>step_one[name]</c>,
/// <c>step_two[start_date]</c>, <c>step_four[opportunities_categories][]</c>).
/// Submitted progressively: each step's fields are validated only when that
/// step is present (Laravel <c>required_with</c>). Step-two <c>uploads</c> are
/// stored via the polymorphic <c>media</c> table; step-three success criteria
/// remain deferred (see docs/SESSION-RESUME.md).
/// </summary>
internal static class EventWriteSupport
{
    /// <summary>Owner discriminator for the <c>media</c> table.</summary>
    public const string EventModelType = "Event";
    public const string UploadsCollection = "uploads";

    /// <summary>Legacy cap for event uploads: 4 MB, jpg/png/pdf.</summary>
    private const long MaxUploadBytes = 4 * 1024 * 1024;

    public static bool IsPrecognitive(HttpContext ctx)
        => ctx.Request.Headers.ContainsKey("Precognition");

    /// <summary>New files posted under <c>step_two[uploads][]</c>.</summary>
    public static IReadOnlyList<IFormFile> Uploads(IFormCollection form)
        => form.Files.GetFiles("step_two[uploads][]");

    public static bool HasStep(IFormCollection form, string step)
        => form.Keys.Any(k => k.StartsWith($"{step}[", StringComparison.OrdinalIgnoreCase));

    public static string? Field(IFormCollection form, string step, string field)
        => form.TryGetValue($"{step}[{field}]", out var v) ? v.ToString() : null;

    public static bool PublishRequested(IFormCollection form)
    {
        var v = Field(form, "step_five", "publish");
        return v is "1" or "true" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> Categories(IFormCollection form)
    {
        var values = new List<string>();
        foreach (var key in form.Keys)
        {
            if (key.StartsWith("step_four[opportunities_categories]", StringComparison.OrdinalIgnoreCase))
            {
                values.AddRange(form[key].Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!));
            }
        }
        return values;
    }

    // ---- validation -------------------------------------------------------

    public static async Task<List<(string Field, string Message)>> ValidateAsync(
        AppDbContext db, IFormCollection form, bool isCreate, CancellationToken ct)
    {
        var errors = new List<(string, string)>();

        if (isCreate && !HasStep(form, "step_one"))
        {
            errors.Add(("step_one", "Step one is required."));
        }

        if (HasStep(form, "step_one"))
        {
            var typeUuid = Field(form, "step_one", "type_uuid");
            if (!Guid.TryParse(typeUuid, out var typeId))
                errors.Add(("step_one.type_uuid", "A valid event type is required."));
            else if (!await db.EventTypes.AnyAsync(t => t.Id == typeId, ct))
                errors.Add(("step_one.type_uuid", "Event type not found."));

            var name = Field(form, "step_one", "name");
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40)
                errors.Add(("step_one.name", "Name is required and must be 40 characters or fewer."));

            var description = Field(form, "step_one", "description");
            if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 300)
                errors.Add(("step_one.description", "Description is required and must be 300 characters or fewer."));

            var seasonId = Field(form, "step_one", "season_id");
            if (!string.IsNullOrWhiteSpace(seasonId))
            {
                if (!Guid.TryParse(seasonId, out var sid))
                    errors.Add(("step_one.season_id", "Invalid season."));
                else if (!await db.Seasons.AnyAsync(s => s.Id == sid, ct))
                    errors.Add(("step_one.season_id", "Season not found."));
            }
        }

        if (HasStep(form, "step_two"))
        {
            if (!TryDecimal(Field(form, "step_two", "lat"), out var lat) || lat is < -90 or > 90)
                errors.Add(("step_two.lat", "Latitude must be between -90 and 90."));
            if (!TryDecimal(Field(form, "step_two", "lon"), out var lon) || lon is < -180 or > 180)
                errors.Add(("step_two.lon", "Longitude must be between -180 and 180."));

            var start = ParseDate(Field(form, "step_two", "start_date"));
            var end = ParseDate(Field(form, "step_two", "end_date"));
            if (start is null) errors.Add(("step_two.start_date", "A valid start date is required."));
            if (end is null) errors.Add(("step_two.end_date", "A valid end date is required."));
            if (start is not null && end is not null && end <= start)
                errors.Add(("step_two.end_date", "End date must be after the start date."));

            if (!int.TryParse(Field(form, "step_two", "min_attendees"), out var min) || min < 1)
                errors.Add(("step_two.min_attendees", "Minimum attendees must be at least 1."));
            else if (!int.TryParse(Field(form, "step_two", "max_attendees"), out var max) || max <= min)
                errors.Add(("step_two.max_attendees", "Maximum attendees must be greater than the minimum."));

            foreach (var file in Uploads(form))
            {
                if (!ProfileAssetSupport.ImageOrPdfContentTypes.Contains(file.ContentType ?? string.Empty))
                    errors.Add(("step_two.uploads", "Uploads must be PDF, JPEG or PNG."));
                if (file.Length > MaxUploadBytes)
                    errors.Add(("step_two.uploads", "Each upload must be 4 MB or smaller."));
            }
        }

        if (HasStep(form, "step_four"))
        {
            var cats = Categories(form);
            if (cats.Count == 0)
            {
                errors.Add(("step_four.opportunities_categories", "At least one category is required."));
            }
            else
            {
                var ids = new List<Guid>();
                foreach (var c in cats)
                {
                    if (Guid.TryParse(c, out var g)) ids.Add(g);
                    else errors.Add(("step_four.opportunities_categories", "Invalid category id."));
                }
                if (ids.Count > 0)
                {
                    var existing = await db.OpportunityCategories
                        .Where(oc => ids.Contains(oc.Id)).Select(oc => oc.Id).ToListAsync(ct);
                    if (existing.Count != ids.Distinct().Count())
                        errors.Add(("step_four.opportunities_categories", "One or more categories do not exist."));
                }
            }
        }

        return errors;
    }

    // ---- apply ------------------------------------------------------------

    public static async Task ApplyStepsAsync(
        AppDbContext db, IFileStorage storage, Event @event, IFormCollection form,
        string? uploadedByUserId, DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (HasStep(form, "step_one"))
        {
            var typeId = Guid.Parse(Field(form, "step_one", "type_uuid")!);
            var seasonId = Guid.TryParse(Field(form, "step_one", "season_id"), out var sid) ? sid : (Guid?)null;
            @event.ApplyBasics(
                typeId,
                Field(form, "step_one", "name")!,
                Field(form, "step_one", "description")!,
                seasonId,
                Field(form, "step_one", "size"),
                Field(form, "step_one", "classification"));
            @event.SetStepsDone(1);
        }

        if (HasStep(form, "step_two"))
        {
            TryDecimal(Field(form, "step_two", "lat"), out var lat);
            TryDecimal(Field(form, "step_two", "lon"), out var lon);
            int? min = int.TryParse(Field(form, "step_two", "min_attendees"), out var mn) ? mn : null;
            int? max = int.TryParse(Field(form, "step_two", "max_attendees"), out var mx) ? mx : null;
            @event.ApplySchedule(
                ParseDate(Field(form, "step_two", "start_date")),
                ParseDate(Field(form, "step_two", "end_date")),
                Field(form, "step_two", "location_title"),
                lat, lon, min, max,
                cityId: null);
            @event.SetStepsDone(2);

            // Replace the event's uploads when new files are posted (the form
            // re-sends files on change; absent files leave existing media alone).
            var files = Uploads(form);
            if (files.Count > 0)
            {
                await MediaSupport.ClearCollectionAsync(
                    db, EventModelType, @event.Id.ToString(), UploadsCollection, ct);
                var order = 0;
                foreach (var file in files)
                {
                    await MediaSupport.AddUploadAsync(
                        db, storage, EventModelType, @event.Id.ToString(), UploadsCollection,
                        file, uploadedByUserId, order++, AssetVisibility.Public, now, ct);
                }
            }
        }

        if (HasStep(form, "step_three")) @event.SetStepsDone(3);

        if (HasStep(form, "step_four"))
        {
            var ids = Categories(form)
                .Select(c => Guid.TryParse(c, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).Distinct().ToList();

            var existing = await db.EventOpportunityCategories
                .Where(p => p.EventId == @event.Id).ToListAsync(ct);
            db.EventOpportunityCategories.RemoveRange(existing);
            foreach (var id in ids)
                db.EventOpportunityCategories.Add(new EventOpportunityCategory(@event.Id, id));

            @event.SetStepsDone(4);
        }

        if (PublishRequested(form)) @event.Publish(today);
    }

    // ---- parsing helpers --------------------------------------------------

    private static bool TryDecimal(string? value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }
}
