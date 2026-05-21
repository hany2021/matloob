using Matloob.Domain.Assets;

namespace Matloob.Api.Features.Assets.UploadAsset;

/// <summary>
/// Shape returned from a successful upload. Mirrors the metadata endpoint
/// so a caller can persist a freshly-uploaded asset id without a follow-up
/// round trip.
///
/// Deliberately excludes <c>RelativePath</c> and <c>StoredFileName</c> — the
/// physical layout is an internal detail of <see cref="IFileStorage"/>.
/// </summary>
public sealed record UploadAssetResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    AssetVisibility Visibility,
    AssetPurpose Purpose,
    DateTimeOffset CreatedAt);
