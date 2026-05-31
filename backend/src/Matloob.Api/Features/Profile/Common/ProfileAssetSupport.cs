using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Features.Profile.Common;

/// <summary>
/// Bridges an inline multipart <see cref="IFormFile"/> (how the legacy
/// frontend submits profile media) onto the new Asset GUID flow: persists the
/// bytes via <see cref="IFileStorage"/>, adds an <see cref="Asset"/> row to the
/// context (NOT yet saved), and returns its id. The caller links the id onto
/// the profile child entity and SaveChanges once for the whole mutation.
/// </summary>
public static class ProfileAssetSupport
{
    public static readonly IReadOnlySet<string> ImageContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/jpg" };

    public static readonly IReadOnlySet<string> ImageOrPdfContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/jpg", "application/pdf" };

    /// <summary>2 MB — the legacy Laravel profile-media cap (max:2048 KB).</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    public static async Task<Guid> SaveAsync(
        AppDbContext db,
        IFileStorage storage,
        string? ownerUserId,
        IFormFile file,
        AssetVisibility visibility,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName ?? string.Empty);

        StoredFileResult stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await storage.SaveAsync(input, ext, ct);
        }

        var asset = new Asset(
            id: Guid.NewGuid(),
            originalFileName: SafeName(file.FileName, ext),
            storedFileName: stored.StoredFileName,
            contentType: string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            sizeBytes: stored.SizeBytes,
            sha256: stored.Sha256Hex,
            relativePath: stored.RelativePath,
            storageDriver: AssetStorageDriver.Local,
            visibility: visibility,
            purpose: AssetPurpose.Generic,
            ownerUserId: ownerUserId,
            ownerEstablishmentId: null,
            metadataJson: null);

        db.Assets.Add(asset);
        return asset.Id;
    }

    private static string SafeName(string? raw, string fallbackExt)
    {
        if (string.IsNullOrWhiteSpace(raw)) return $"upload{fallbackExt}";
        var leaf = Path.GetFileName(raw.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf)) return $"upload{fallbackExt}";
        return leaf.Length > 500 ? leaf[..500] : leaf;
    }
}
