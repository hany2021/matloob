using System.Globalization;
using System.Text.RegularExpressions;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Matloob.Domain.Events;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events.Common;

/// <summary>
/// Parses / validates / persists the event wizard's nested
/// <c>opportunities[]</c> array (legacy <c>EventService::handleOpportunities</c>
/// → <c>OpportunityService::store</c>): each item creates an Opportunity linked
/// to the event, attaches its category to the event, stores uploads
/// (<c>opportunity_assets</c>), and — for non-vacancy categories — its nested
/// success criteria. Re-submitting replaces the event's opportunities.
/// </summary>
internal static partial class EventOpportunitiesSupport
{
    private const long MaxUploadBytes = 4 * 1024 * 1024;

    [GeneratedRegex(@"^opportunities\[(\d+)\]\[(\w+)\]$")]
    private static partial Regex ScalarKey();

    [GeneratedRegex(@"^opportunities\[(\d+)\]\[(establishment_classification|gender)\]\[\d*\]$")]
    private static partial Regex ArrayKey();

    [GeneratedRegex(@"^opportunities\[(\d+)\]\[uploads\]")]
    private static partial Regex UploadKey();

    public sealed class OpportunityInput
    {
        public int Index { get; init; }
        public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Classifications { get; } = new();
        public List<string> Genders { get; } = new();
        public List<IFormFile> Files { get; } = new();
        public IReadOnlyList<SuccessCriteriaSupport.CriterionInput> Criteria { get; set; } =
            new List<SuccessCriteriaSupport.CriterionInput>();

        public string? Field(string name) => Fields.GetValueOrDefault(name);
    }

    public static bool HasOpportunities(IFormCollection form)
        => form.Keys.Any(k => k.StartsWith("opportunities[", StringComparison.OrdinalIgnoreCase))
           || form.Files.Any(f => f.Name.StartsWith("opportunities[", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<OpportunityInput> Parse(IFormCollection form)
    {
        var byIndex = new SortedDictionary<int, OpportunityInput>();
        OpportunityInput At(int i) =>
            byIndex.TryGetValue(i, out var o) ? o : byIndex[i] = new OpportunityInput { Index = i };

        foreach (var (key, value) in form)
        {
            var arr = ArrayKey().Match(key);
            if (arr.Success)
            {
                var item = At(int.Parse(arr.Groups[1].Value));
                if (arr.Groups[2].Value.Equals("gender", StringComparison.OrdinalIgnoreCase))
                    item.Genders.AddRange(value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!));
                else
                    item.Classifications.AddRange(value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!));
                continue;
            }
            var sc = ScalarKey().Match(key);
            if (sc.Success)
                At(int.Parse(sc.Groups[1].Value)).Fields[sc.Groups[2].Value] = value.ToString();
        }

        foreach (var file in form.Files)
        {
            var m = UploadKey().Match(file.Name);
            if (m.Success) At(int.Parse(m.Groups[1].Value)).Files.Add(file);
        }

        foreach (var item in byIndex.Values)
            item.Criteria = SuccessCriteriaSupport.Parse(form, $"opportunities[{item.Index}][success_criteria]");

        return byIndex.Values.ToList();
    }

    public static async Task ValidateAsync(
        AppDbContext db, IReadOnlyList<OpportunityInput> items, List<(string, string)> errors,
        CancellationToken ct, DateOnly? eventStart = null, DateOnly? eventEnd = null)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var o = items[i];
            var p = $"opportunities.{i}";

            // Resolve the category up front — its for_vacancy flag drives the
            // type-aware required-field rules below.
            OpportunityCategory? category = null;
            if (!Guid.TryParse(o.Field("opportunity_category_uuid") ?? o.Field("opportunity_category_id"), out var catId))
                errors.Add(($"{p}.opportunity_category_uuid", "A valid opportunity category is required."));
            else
            {
                category = await db.OpportunityCategories.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == catId, ct);
                if (category is null)
                    errors.Add(($"{p}.opportunity_category_uuid", "Opportunity category not found."));
            }

            if (string.IsNullOrWhiteSpace(o.Field("name")) || o.Field("name")!.Trim().Length > 120)
                errors.Add(($"{p}.name", "Name is required and must be 120 characters or fewer."));
            if (string.IsNullOrWhiteSpace(o.Field("description")) || o.Field("description")!.Trim().Length < 10)
                errors.Add(($"{p}.description", "Description is required (min 10 characters)."));

            var startDate = ParseDate(o.Field("start_date"));
            var endDate = ParseDate(o.Field("end_date"));
            if (startDate is null)
                errors.Add(($"{p}.start_date", "A valid start date is required."));
            if (endDate is null)
                errors.Add(($"{p}.end_date", "A valid end date is required."));
            // Ordering (legacy `after:start_date`) — a clean 422 instead of the
            // domain factory's ArgumentException (which would surface as a 500).
            if (startDate is not null && endDate is not null && endDate <= startDate)
                errors.Add(($"{p}.end_date", "End date must be after the start date."));
            // Within the owning event's window (legacy ValidOpportunityStart/EndDate).
            // Skipped when the event's dates aren't known yet (e.g. early wizard
            // validation pings before step_two is filled).
            if (startDate is not null && eventStart is not null && startDate < eventStart)
                errors.Add(($"{p}.start_date", "Start date must be on or after the event's start date."));
            if (endDate is not null && eventEnd is not null && endDate > eventEnd)
                errors.Add(($"{p}.end_date", "End date must be on or before the event's end date."));

            if (!TryDecimal(o.Field("lat"), out var lat) || lat is < -90 or > 90)
                errors.Add(($"{p}.lat", "Latitude must be between -90 and 90."));
            if (!TryDecimal(o.Field("lon"), out var lon) || lon is < -180 or > 180)
                errors.Add(($"{p}.lon", "Longitude must be between -180 and 180."));
            if (!int.TryParse(o.Field("required_personnel"), out var rp) || rp < 1)
                errors.Add(($"{p}.required_personnel", "Required personnel must be at least 1."));

            // Type-aware required fields (legacy RequiredForVacancy /
            // RequiredForOpportunity): a vacancy needs a monthly salary; a
            // non-vacancy needs at least one success criterion. The frontend is
            // laravel-precognition (server-driven validation), so it relies on
            // these 422s to block an incomplete submit.
            if (category is not null)
            {
                if (category.ForVacancy)
                {
                    var salaryRaw = o.Field("monthly_salary");
                    if (string.IsNullOrWhiteSpace(salaryRaw)
                        || !decimal.TryParse(salaryRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var salary)
                        || salary < 0)
                        errors.Add(($"{p}.monthly_salary", "Monthly salary is required for a vacancy opportunity."));
                }
                else if (o.Criteria.Count == 0)
                {
                    errors.Add(($"{p}.success_criteria", "At least one success criterion is required."));
                }
            }

            foreach (var f in o.Files)
            {
                if (!ProfileAssetSupport.ImageOrPdfContentTypes.Contains(f.ContentType ?? string.Empty))
                    errors.Add(($"{p}.uploads", "Uploads must be PDF, JPEG or PNG."));
                if (f.Length > MaxUploadBytes)
                    errors.Add(($"{p}.uploads", "Each upload must be 4 MB or smaller."));
            }

            SuccessCriteriaSupport.Validate(o.Criteria, $"{p}.success_criteria", errors);
        }
    }

    /// <summary>Replace the event's opportunities with the parsed set (wizard step-4).</summary>
    public static async Task ReplaceAsync(
        AppDbContext db, IFileStorage storage, Event @event, IReadOnlyList<OpportunityInput> items,
        Guid establishmentId, string? uploadedByUserId, DateTimeOffset now, CancellationToken ct)
    {
        await DeleteExistingAsync(db, @event.Id, ct);
        await AddAsync(db, storage, @event.Id, items, establishmentId, uploadedByUserId, now, ct);
    }

    /// <summary>
    /// Create the parsed opportunities under <paramref name="eventId"/> WITHOUT
    /// removing any existing ones — the standalone "add opportunities to an
    /// existing event" path (legacy <c>OpportunityService::store</c>). Returns
    /// the created aggregates so the caller can emit per-opportunity events.
    /// </summary>
    public static async Task<List<Opportunity>> AddAsync(
        AppDbContext db, IFileStorage storage, Guid eventId, IReadOnlyList<OpportunityInput> items,
        Guid establishmentId, string? uploadedByUserId, DateTimeOffset now, CancellationToken ct)
    {
        // Event's current category pivot — attach any newly-referenced categories.
        // Include BOTH persisted rows AND ones already staged on the change tracker
        // (step_four, in the SAME request, has typically just added this event's
        // categories), since re-adding the same {EventId,OpportunityCategoryId}
        // composite key throws a duplicate-tracking error. Ignore tracked rows
        // marked for deletion so a re-selected category is still re-attached.
        var eventCategoryIds = (await db.EventOpportunityCategories
            .Where(p => p.EventId == eventId).Select(p => p.OpportunityCategoryId).ToListAsync(ct))
            .Concat(db.EventOpportunityCategories.Local
                .Where(p => p.EventId == eventId
                            && db.Entry(p).State != EntityState.Deleted)
                .Select(p => p.OpportunityCategoryId))
            .ToHashSet();

        var created = new List<Opportunity>(items.Count);

        foreach (var input in items)
        {
            var categoryId = Guid.Parse(input.Field("opportunity_category_uuid") ?? input.Field("opportunity_category_id")!);
            var category = await db.OpportunityCategories.AsNoTracking().FirstAsync(c => c.Id == categoryId, ct);

            Guid? nationalityId = Guid.TryParse(input.Field("nationality"), out var nid) ? nid : null;
            Guid? cityId = Guid.TryParse(input.Field("city_id"), out var cid) ? cid : null;

            var opportunity = OpportunityBuildSupport.Build(
                establishmentId,
                eventId,
                categoryId,
                input.Field("name"),
                input.Field("description"),
                ParseDate(input.Field("start_date")),
                ParseDate(input.Field("end_date")),
                input.Field("location_title"),
                TryDecimal(input.Field("lat"), out var lat) ? lat : 0m,
                TryDecimal(input.Field("lon"), out var lon) ? lon : 0m,
                int.TryParse(input.Field("required_personnel"), out var rp) ? rp : 1,
                now,
                cityId,
                nationalityId,
                TryDecimal(input.Field("monthly_salary"), out var ms) ? ms : null,
                byte.TryParse(input.Field("years_of_experience_required"), out var ye) ? ye : null,
                input.Field("working_hours_type"),
                input.Field("working_hours_from"),
                input.Field("working_hours_to"),
                TryDecimal(input.Field("fees"), out var fee) ? fee : null,
                input.Field("phone_contact_information"),
                input.Field("email_contact_information"),
                input.Classifications.Count > 0 ? input.Classifications : null,
                input.Genders.Count > 0 ? input.Genders : null);

            db.Opportunities.Add(opportunity);
            created.Add(opportunity);

            if (eventCategoryIds.Add(categoryId))
                db.EventOpportunityCategories.Add(new EventOpportunityCategory(eventId, categoryId));

            foreach (var file in input.Files)
            {
                var assetId = await ProfileAssetSupport.SaveAsync(
                    db, storage, uploadedByUserId, file, AssetVisibility.Public, ct);
                db.OpportunityAssets.Add(new OpportunityAsset(
                    Guid.NewGuid(), opportunity.Id, assetId, uploadedByUserId ?? string.Empty, now));
            }

            // Success criteria only apply to non-vacancy categories (legacy rule).
            if (!category.ForVacancy && input.Criteria.Count > 0)
                await SuccessCriteriaSupport.AddForOpportunityAsync(
                    db, storage, opportunity.Id, input.Criteria, uploadedByUserId, now, ct);
        }

        return created;
    }

    private static async Task DeleteExistingAsync(AppDbContext db, Guid eventId, CancellationToken ct)
    {
        var oppIds = await db.Opportunities.Where(o => o.EventId == eventId).Select(o => o.Id).ToListAsync(ct);
        if (oppIds.Count == 0) return;

        var critIds = await db.SuccessManagementCriteria
            .Where(c => c.OpportunityId != null && oppIds.Contains(c.OpportunityId.Value))
            .Select(c => c.Id).ToListAsync(ct);
        if (critIds.Count > 0)
        {
            db.SuccessManagementCriterionAssets.RemoveRange(
                await db.SuccessManagementCriterionAssets
                    .Where(a => critIds.Contains(a.SuccessManagementCriterionId)).ToListAsync(ct));
            db.SuccessManagementCriteria.RemoveRange(
                await db.SuccessManagementCriteria.Where(c => critIds.Contains(c.Id)).ToListAsync(ct));
        }
        db.OpportunityAssets.RemoveRange(
            await db.OpportunityAssets.Where(a => oppIds.Contains(a.OpportunityId)).ToListAsync(ct));
        db.Opportunities.RemoveRange(
            await db.Opportunities.Where(o => oppIds.Contains(o.Id)).ToListAsync(ct));
    }

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
