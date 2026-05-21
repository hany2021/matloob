using System.Text.Json.Serialization;

namespace Matloob.Domain.Assets;

/// <summary>
/// Which physical store holds the bytes for this asset.
///
/// v1 ships only <see cref="Local"/> (filesystem); the second implementation
/// (S3 / MinIO / Azure Blob) is intentionally deferred — see open question
/// O-4 in docs/15-establishment-onboarding-spec.md. The column exists now so
/// the move later is a no-op migration, not a schema change.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetStorageDriver>))]
public enum AssetStorageDriver
{
    /// <summary>Local filesystem under <c>Storage:AssetsRoot</c>.</summary>
    Local = 0,
}
