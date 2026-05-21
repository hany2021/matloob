namespace Matloob.Api.Features.Assets.UploadAsset;

/// <summary>
/// Centralized whitelist of MIME types the public upload endpoint accepts.
/// Single source of truth so the validator, the endpoint, and tests cannot
/// drift.
///
/// Source: docs/15-establishment-onboarding-spec.md §3.3 (PDF / JPEG / PNG,
/// ≤ 10 MB). Any future per-purpose narrowing (e.g. CR documents must be
/// PDF only) belongs here.
/// </summary>
internal static class AllowedAssetUploadContentTypes
{
    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Pdf,
            Jpeg,
            Png,
        };

    /// <summary>Allowed file-name extensions, lower-case with leading dot.</summary>
    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".jpg",
            ".jpeg",
            ".png",
        };
}
