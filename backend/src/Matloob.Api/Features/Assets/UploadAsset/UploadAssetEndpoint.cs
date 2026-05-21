using FastEndpoints;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Features.Assets.UploadAsset;

/// <summary>
/// <c>POST /api/v1/assets</c> — multipart upload that creates a new Asset
/// row and writes the bytes through <see cref="IFileStorage"/>.
///
/// Auth: any authenticated principal. No role gate yet (the spec lets every
/// onboarding user upload their own AuthorizationLetter /
/// CommercialRegistration). The owner_user_id column captures the uploader
/// so later endpoints can enforce per-asset ownership.
///
/// Validation runs before bytes are saved:
///   - File is required, non-empty, and at or under <c>Storage:MaxUploadBytes</c>.
///   - Content-type must be in <see cref="AllowedAssetUploadContentTypes.All"/>.
///   - File extension must be in <see cref="AllowedAssetUploadContentTypes.AllowedExtensions"/>.
///
/// Order matters: we validate first (no bytes touched), then stream to disk,
/// then commit the row. A failure after the bytes hit disk leaves an orphan
/// file — orphan cleanup is a future cron job (see
/// docs/15-establishment-onboarding-spec.md §11).
/// </summary>
public sealed class UploadAssetEndpoint : Endpoint<UploadAssetRequest, UploadAssetResponse>
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly FileStorageOptions _options;
    private readonly TimeProvider _clock;

    public UploadAssetEndpoint(
        AppDbContext db,
        IFileStorage storage,
        ICurrentUser currentUser,
        IOptions<FileStorageOptions> options,
        TimeProvider clock)
    {
        _db = db;
        _storage = storage;
        _currentUser = currentUser;
        _options = options.Value;
        _clock = clock;
    }

    public override void Configure()
    {
        Post("/api/v1/assets");
        // Default FastEndpoints policy already requires authentication.
        // No Policies(...) call: any authenticated bearer works.
        AllowFileUploads();
        Description(b => b
            .Accepts<UploadAssetRequest>("multipart/form-data")
            .Produces<UploadAssetResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413RequestEntityTooLarge)
            .WithTags("Assets"));
        Summary(s =>
        {
            s.Summary = "Upload a file. Returns the Asset GUID and metadata.";
            s.Description =
                "Multipart/form-data only. Authenticated. Allowed types: " +
                "application/pdf, image/jpeg, image/png. Max size from " +
                "Storage:MaxUploadBytes (default 10 MB).";
        });
    }

    public override async Task HandleAsync(UploadAssetRequest req, CancellationToken ct)
    {
        var file = req.File;

        // -- validation -----------------------------------------------------
        if (file is null || file.Length == 0)
        {
            AddError(r => r.File, "A non-empty file is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (file.Length > _options.MaxUploadBytes)
        {
            AddError(r => r.File,
                $"File exceeds maximum size of {_options.MaxUploadBytes:N0} bytes.");
            await Send.ErrorsAsync(StatusCodes.Status413RequestEntityTooLarge, ct);
            return;
        }

        var contentType = file.ContentType ?? string.Empty;
        if (!AllowedAssetUploadContentTypes.All.Contains(contentType))
        {
            AddError(r => r.File,
                $"Content-Type '{contentType}' is not allowed. " +
                $"Allowed: {string.Join(", ", AllowedAssetUploadContentTypes.All)}.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var ext = Path.GetExtension(file.FileName ?? string.Empty);
        if (!AllowedAssetUploadContentTypes.AllowedExtensions.Contains(ext))
        {
            AddError(r => r.File,
                $"File extension '{ext}' is not allowed. " +
                $"Allowed: {string.Join(", ", AllowedAssetUploadContentTypes.AllowedExtensions)}.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        // -- storage --------------------------------------------------------
        StoredFileResult stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await _storage.SaveAsync(input, ext, ct);
        }

        // -- DB row ---------------------------------------------------------
        var asset = new Asset(
            id: Guid.NewGuid(),
            originalFileName: SafeFileName(file.FileName, fallbackExt: ext),
            storedFileName: stored.StoredFileName,
            contentType: contentType,
            sizeBytes: stored.SizeBytes,
            sha256: stored.Sha256Hex,
            relativePath: stored.RelativePath,
            storageDriver: AssetStorageDriver.Local,
            visibility: req.Visibility,
            purpose: req.Purpose,
            ownerUserId: _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ownerEstablishmentId: req.OwnerEstablishmentId,
            metadataJson: null);

        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(ct);

        // -- response -------------------------------------------------------
        var response = new UploadAssetResponse(
            Id: asset.Id,
            OriginalFileName: asset.OriginalFileName,
            ContentType: asset.ContentType,
            SizeBytes: asset.SizeBytes,
            Sha256: asset.Sha256,
            Visibility: asset.Visibility,
            Purpose: asset.Purpose,
            CreatedAt: asset.CreatedAt);

        // 201 + Location header so callers can follow REST conventions
        // without needing to know the GET-asset endpoint's framework name.
        HttpContext.Response.Headers.Location = $"/api/v1/assets/{asset.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }

    /// <summary>
    /// Reduce a user-supplied file name to a safe basename + sanitized
    /// extension. Strips any path separators (defence in depth — IFormFile
    /// already does this, but a misconfigured proxy could change that).
    /// </summary>
    private static string SafeFileName(string? raw, string fallbackExt)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"upload{fallbackExt}";
        }

        // GetFileName strips any directory components on either platform.
        var leaf = Path.GetFileName(raw.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return $"upload{fallbackExt}";
        }

        // Cap so a malicious 1 MB filename doesn't bloat the DB column.
        return leaf.Length > 500 ? leaf[..500] : leaf;
    }
}
