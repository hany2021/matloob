using System.Text.Json.Serialization;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Common;

/// <summary>
/// Polymorphic attachment helper over the <see cref="Media"/> link table — the
/// new system's <c>media</c> equivalent. Any owner attaches files via
/// (<c>modelType</c>, <c>modelId</c>, <c>collection</c>): the bytes go through
/// the canonical Asset flow (<see cref="ProfileAssetSupport.SaveAsync"/>), then
/// a Media row links the Asset to the owner. Staged on the context — the caller
/// SaveChanges once.
/// </summary>
public static class MediaSupport
{
    /// <summary>Persist one uploaded file and link it to the owner's collection.</summary>
    public static async Task<Guid> AddUploadAsync(
        AppDbContext db, IFileStorage storage,
        string modelType, string modelId, string collection,
        IFormFile file, string? uploadedByUserId, int order,
        AssetVisibility visibility, DateTimeOffset now, CancellationToken ct)
    {
        var assetId = await ProfileAssetSupport.SaveAsync(db, storage, uploadedByUserId, file, visibility, ct);
        db.Media.Add(new Media(
            Guid.NewGuid(), assetId, modelType, modelId, collection, order, uploadedByUserId, now));
        return assetId;
    }

    /// <summary>Remove (soft-delete) all of an owner's media in a collection — replace semantics.</summary>
    public static async Task ClearCollectionAsync(
        AppDbContext db, string modelType, string modelId, string collection, CancellationToken ct)
    {
        var existing = await db.Media
            .Where(m => m.ModelType == modelType && m.ModelId == modelId && m.CollectionName == collection)
            .ToListAsync(ct);
        db.Media.RemoveRange(existing);
    }

    /// <summary>
    /// Project an owner-collection's media (joined to their assets) for a set of
    /// owners, grouped by <c>modelId</c>. Used by read mappers to fill the
    /// <c>uploads</c> arrays without N+1.
    /// </summary>
    public static async Task<Dictionary<string, List<MediaDto>>> ListForOwnersAsync(
        AppDbContext db, string modelType, IReadOnlyCollection<string> modelIds,
        string collection, CancellationToken ct)
    {
        if (modelIds.Count == 0) return new Dictionary<string, List<MediaDto>>();

        var rows = await (
            from m in db.Media.AsNoTracking()
            where m.ModelType == modelType && modelIds.Contains(m.ModelId) && m.CollectionName == collection
            join a in db.Assets.AsNoTracking() on m.AssetId equals a.Id
            orderby m.OrderColumn
            select new { m.ModelId, MediaId = m.Id, a.OriginalFileName, AssetId = a.Id })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ModelId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new MediaDto(
                    r.MediaId, r.OriginalFileName, $"/api/v1/assets/{r.AssetId}")).ToList());
    }

    /// <summary>Project a single owner's media collection.</summary>
    public static async Task<List<MediaDto>> ListAsync(
        AppDbContext db, string modelType, string modelId, string collection, CancellationToken ct)
    {
        var byOwner = await ListForOwnersAsync(db, modelType, new[] { modelId }, collection, ct);
        return byOwner.GetValueOrDefault(modelId) ?? new List<MediaDto>();
    }
}

/// <summary>Wire shape of one attachment — mirrors the legacy Spatie MediaResource.</summary>
public sealed record MediaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url);
