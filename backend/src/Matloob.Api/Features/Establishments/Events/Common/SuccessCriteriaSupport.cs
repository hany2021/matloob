using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Matloob.Domain.Opportunities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events.Common;

/// <summary>
/// Parses / validates / persists / projects success-management criteria that the
/// public frontend submits as nested multipart arrays under a prefix such as
/// <c>step_three[success_criteria]</c> (event) or
/// <c>opportunities[0][success_criteria]</c> (nested opportunity). Each criterion
/// carries <c>output</c> / <c>success_criteria</c> / <c>comment</c> plus an
/// <c>uploads[]</c> file sub-array linked through
/// <see cref="SuccessManagementCriterionAsset"/> (the legacy per-owner join).
/// </summary>
internal static class SuccessCriteriaSupport
{
    private const long MaxUploadBytes = 4 * 1024 * 1024; // legacy 4 MB

    public sealed class CriterionInput
    {
        public string? Output { get; set; }
        public string? SuccessCriteria { get; set; }
        public string? Comment { get; set; }
        public List<IFormFile> Files { get; } = new();
    }

    /// <summary>
    /// Pull criterion items out of the form for the given bracket prefix
    /// (e.g. <c>step_three[success_criteria]</c>), ordered by index.
    /// </summary>
    public static IReadOnlyList<CriterionInput> Parse(IFormCollection form, string prefix)
    {
        var escaped = Regex.Escape(prefix);
        var scalar = new Regex($@"^{escaped}\[(\d+)\]\[(output|success_criteria|comment)\]$",
            RegexOptions.IgnoreCase);
        var uploads = new Regex($@"^{escaped}\[(\d+)\]\[uploads\]", RegexOptions.IgnoreCase);

        var byIndex = new SortedDictionary<int, CriterionInput>();
        CriterionInput At(int i) =>
            byIndex.TryGetValue(i, out var c) ? c : byIndex[i] = new CriterionInput();

        foreach (var (key, value) in form)
        {
            var m = scalar.Match(key);
            if (!m.Success) continue;
            var item = At(int.Parse(m.Groups[1].Value));
            switch (m.Groups[2].Value.ToLowerInvariant())
            {
                case "output": item.Output = value.ToString(); break;
                case "success_criteria": item.SuccessCriteria = value.ToString(); break;
                case "comment": item.Comment = value.ToString(); break;
            }
        }

        foreach (var file in form.Files)
        {
            var m = uploads.Match(file.Name);
            if (m.Success) At(int.Parse(m.Groups[1].Value)).Files.Add(file);
        }

        return byIndex.Values.ToList();
    }

    /// <summary>Validate a parsed criteria set, prefixing each error field.</summary>
    public static void Validate(
        IReadOnlyList<CriterionInput> items, string fieldPrefix, List<(string, string)> errors)
    {
        foreach (var c in items)
        {
            var output = c.Output?.Trim() ?? string.Empty;
            if (output.Length is < 10 or > 40)
                errors.Add(($"{fieldPrefix}.output", "Output is required and must be 10–40 characters."));

            var sc = c.SuccessCriteria?.Trim() ?? string.Empty;
            if (sc.Length is < 30 or > 1000)
                errors.Add(($"{fieldPrefix}.success_criteria", "Success criteria is required and must be 30–1000 characters."));

            var comment = c.Comment?.Trim();
            if (!string.IsNullOrEmpty(comment) && comment.Length is < 30 or > 1000)
                errors.Add(($"{fieldPrefix}.comment", "Comment must be 30–1000 characters."));

            foreach (var f in c.Files)
            {
                if (!ProfileAssetSupport.ImageOrPdfContentTypes.Contains(f.ContentType ?? string.Empty))
                    errors.Add(($"{fieldPrefix}.uploads", "Uploads must be PDF, JPEG or PNG."));
                if (f.Length > MaxUploadBytes)
                    errors.Add(($"{fieldPrefix}.uploads", "Each upload must be 4 MB or smaller."));
            }
        }
    }

    /// <summary>
    /// Replace the event's criteria with <paramref name="items"/> (delete-then-create,
    /// matching the legacy step-three semantics), persisting any uploads.
    /// </summary>
    public static async Task ReplaceForEventAsync(
        AppDbContext db, IFileStorage storage, Guid eventId,
        IReadOnlyList<CriterionInput> items, string? uploadedByUserId,
        DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.SuccessManagementCriteria
            .Where(c => c.EventId == eventId).Select(c => c.Id).ToListAsync(ct);
        await DeleteCriteriaAsync(db, existing, ct);

        foreach (var input in items)
        {
            var criterion = SuccessManagementCriterion.ForEvent(
                Guid.NewGuid(), eventId, input.Output!, input.SuccessCriteria!, input.Comment);
            db.SuccessManagementCriteria.Add(criterion);
            await AddUploadsAsync(db, storage, criterion.Id, input.Files, uploadedByUserId, now, ct);
        }
    }

    /// <summary>Persist a single criterion's uploads (Asset + criterion-asset join).</summary>
    public static async Task AddUploadsAsync(
        AppDbContext db, IFileStorage storage, Guid criterionId,
        IReadOnlyList<IFormFile> files, string? uploadedByUserId,
        DateTimeOffset now, CancellationToken ct)
    {
        foreach (var file in files)
        {
            var assetId = await ProfileAssetSupport.SaveAsync(
                db, storage, uploadedByUserId, file, AssetVisibility.Public, ct);
            db.SuccessManagementCriterionAssets.Add(new SuccessManagementCriterionAsset(
                Guid.NewGuid(), criterionId, assetId, uploadedByUserId ?? string.Empty, now));
        }
    }

    private static async Task DeleteCriteriaAsync(AppDbContext db, List<Guid> criterionIds, CancellationToken ct)
    {
        if (criterionIds.Count == 0) return;
        var assets = await db.SuccessManagementCriterionAssets
            .Where(a => criterionIds.Contains(a.SuccessManagementCriterionId)).ToListAsync(ct);
        db.SuccessManagementCriterionAssets.RemoveRange(assets);
        var criteria = await db.SuccessManagementCriteria
            .Where(c => criterionIds.Contains(c.Id)).ToListAsync(ct);
        db.SuccessManagementCriteria.RemoveRange(criteria);
    }

    /// <summary>Project an event's criteria (with uploads) in the frontend shape.</summary>
    public static Task<List<CriterionDto>> ProjectForEventAsync(
        AppDbContext db, Guid eventId, CancellationToken ct)
        => ProjectAsync(db, db.SuccessManagementCriteria.Where(c => c.EventId == eventId), ct);

    /// <summary>Project an opportunity's criteria (with uploads) in the frontend shape.</summary>
    public static Task<List<CriterionDto>> ProjectForOpportunityAsync(
        AppDbContext db, Guid opportunityId, CancellationToken ct)
        => ProjectAsync(db, db.SuccessManagementCriteria.Where(c => c.OpportunityId == opportunityId), ct);

    /// <summary>Batch-project criteria for many events, grouped by event id (no N+1).</summary>
    public static async Task<Dictionary<Guid, List<CriterionDto>>> ProjectForEventsAsync(
        AppDbContext db, IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, List<CriterionDto>>();
        if (eventIds.Count == 0) return result;

        var criteria = await db.SuccessManagementCriteria.AsNoTracking()
            .Where(c => c.EventId != null && eventIds.Contains(c.EventId.Value))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        if (criteria.Count == 0) return result;

        var ids = criteria.Select(c => c.Id).ToList();
        var assetRows = await (
            from a in db.SuccessManagementCriterionAssets.AsNoTracking()
            where ids.Contains(a.SuccessManagementCriterionId)
            join asset in db.Assets.AsNoTracking() on a.AssetId equals asset.Id
            orderby a.UploadedAt
            select new { a.SuccessManagementCriterionId, asset.Id, asset.OriginalFileName })
            .ToListAsync(ct);
        var uploadsByCriterion = assetRows
            .GroupBy(r => r.SuccessManagementCriterionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new CriterionUploadDto(r.Id, r.OriginalFileName, $"/api/v1/assets/{r.Id}")).ToList());

        return criteria
            .GroupBy(c => c.EventId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => new CriterionDto(
                    c.Id, c.Id, c.Output, c.SuccessCriteria, c.Comment,
                    uploadsByCriterion.GetValueOrDefault(c.Id) ?? new List<CriterionUploadDto>())).ToList());
    }

    private static async Task<List<CriterionDto>> ProjectAsync(
        AppDbContext db, IQueryable<SuccessManagementCriterion> query, CancellationToken ct)
    {
        var criteria = await query.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(ct);
        if (criteria.Count == 0) return new List<CriterionDto>();

        var ids = criteria.Select(c => c.Id).ToList();
        var assetRows = await (
            from a in db.SuccessManagementCriterionAssets.AsNoTracking()
            where ids.Contains(a.SuccessManagementCriterionId)
            join asset in db.Assets.AsNoTracking() on a.AssetId equals asset.Id
            orderby a.UploadedAt
            select new { a.SuccessManagementCriterionId, asset.Id, asset.OriginalFileName })
            .ToListAsync(ct);
        var uploadsByCriterion = assetRows
            .GroupBy(r => r.SuccessManagementCriterionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new CriterionUploadDto(r.Id, r.OriginalFileName, $"/api/v1/assets/{r.Id}")).ToList());

        return criteria
            .Select(c => new CriterionDto(
                c.Id, c.Id, c.Output, c.SuccessCriteria, c.Comment,
                uploadsByCriterion.GetValueOrDefault(c.Id) ?? new List<CriterionUploadDto>()))
            .ToList();
    }
}

/// <summary>Mirrors the legacy <c>SuccessManagementCriterionResource</c> + frontend <c>SuccessCriteria</c>.</summary>
public sealed record CriterionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("uuid")] Guid Uuid,
    [property: JsonPropertyName("output")] string Output,
    [property: JsonPropertyName("success_criteria")] string SuccessCriteria,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("uploads")] IReadOnlyList<CriterionUploadDto> Uploads);

public sealed record CriterionUploadDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url);
