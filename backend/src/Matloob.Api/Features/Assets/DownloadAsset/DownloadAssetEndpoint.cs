using System.Net.Mime;
using FastEndpoints;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Assets.DownloadAsset;

/// <summary>
/// <c>GET /api/v1/assets/{id}</c> — streams the bytes for an asset.
///
/// Auth: public assets allow anonymous; private assets require the uploader
/// OR a matloob_admin. See <see cref="AssetAccessRules"/>.
///
/// The response Content-Type is taken from the DB row (recorded at upload
/// time). Content-Disposition is <c>inline</c> with a sanitized filename so
/// the browser can render PDFs / images in-place; downstream callers that
/// want a forced save can override with <c>?download=1</c> in a later phase.
///
/// Physical path is never exposed — the file is streamed through
/// <see cref="IFileStorage.OpenReadAsync"/>.
/// </summary>
public sealed class DownloadAssetEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;

    public DownloadAssetEndpoint(AppDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public override void Configure()
    {
        Get("/api/v1/assets/{id}");
        AllowAnonymous(); // per-asset auth handled in HandleAsync.
        Description(b => b
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Assets"));
        Summary(s =>
        {
            s.Summary = "Download an asset's bytes by GUID.";
            s.Description =
                "Public assets are anonymous. Private assets require the " +
                "uploader OR a matloob_admin. Streamed; no physical path " +
                "is ever exposed.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var asset = await _db.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (asset is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var verdict = await AssetAccessRules.CanReadAsync(asset, User, _db, ct);
        switch (verdict)
        {
            case AssetAccessRules.AccessVerdict.Unauthenticated:
                await Send.UnauthorizedAsync(ct);
                return;
            case AssetAccessRules.AccessVerdict.Forbidden:
                await Send.ForbiddenAsync(ct);
                return;
        }

        // The DB row points at a file; if it isn't on disk, that's a
        // bytes-vs-metadata divergence. Treat the same as not-found from the
        // caller's perspective so we don't leak the partial state.
        if (!await _storage.ExistsAsync(asset.RelativePath, ct))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Set Content-Disposition manually so we can use the ORIGINAL file
        // name, not the GUID stored leaf, and keep it sanitized via
        // ContentDisposition. Using "inline" lets browsers preview PDFs/images;
        // callers that need a forced download can use the metadata + a
        // ?download=1 toggle in a future iteration.
        var disposition = new ContentDisposition
        {
            FileName = asset.OriginalFileName,
            Inline = true,
        }.ToString();
        HttpContext.Response.Headers.ContentDisposition = disposition;

        // Cache headers: signed-URL / per-asset cache policy lands when the
        // signed-URL endpoint arrives (out of scope this phase). For now,
        // keep responses uncached so a soft-delete is visible immediately.
        HttpContext.Response.Headers.CacheControl = "no-store";

        var stream = await _storage.OpenReadAsync(asset.RelativePath, ct);
        // FastEndpoints owns disposal once we hand the stream to Send.StreamAsync.
        await Send.StreamAsync(
            stream,
            fileName: asset.OriginalFileName,
            fileLengthBytes: asset.SizeBytes,
            contentType: asset.ContentType,
            cancellation: ct);
    }
}
