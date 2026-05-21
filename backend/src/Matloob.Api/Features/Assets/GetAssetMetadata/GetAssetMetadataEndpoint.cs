using FastEndpoints;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Assets.GetAssetMetadata;

/// <summary>
/// <c>GET /api/v1/assets/{id}/metadata</c> — returns the public-safe shape
/// of an asset row.
///
/// Auth: public assets allow anonymous; private assets require an
/// authenticated owner OR an admin. See <see cref="AssetAccessRules"/>.
///
/// Soft-deleted assets: indistinguishable from "never existed" — 404 in
/// both cases. This is intentional; it avoids leaking the fact that a
/// deleted asset once existed.
/// </summary>
public sealed class GetAssetMetadataEndpoint : EndpointWithoutRequest<GetAssetMetadataResponse>
{
    private readonly AppDbContext _db;

    public GetAssetMetadataEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/assets/{id}/metadata");
        AllowAnonymous(); // per-asset auth handled in HandleAsync.
        Description(b => b
            .Produces<GetAssetMetadataResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Assets"));
        Summary(s =>
        {
            s.Summary = "Get the metadata for an asset by GUID.";
            s.Description =
                "Public assets are anonymous. Private assets require the " +
                "uploader OR a matloob_admin. Soft-deleted assets return 404.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        // Global soft-delete query filter ensures we never see deleted rows.
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

        var response = new GetAssetMetadataResponse(
            Id: asset.Id,
            OriginalFileName: asset.OriginalFileName,
            ContentType: asset.ContentType,
            SizeBytes: asset.SizeBytes,
            Sha256: asset.Sha256,
            Visibility: asset.Visibility,
            Purpose: asset.Purpose,
            CreatedAt: asset.CreatedAt);

        await Send.OkAsync(response, ct);
    }
}

/// <summary>
/// Public-safe view of an asset row. Identical shape to
/// <see cref="UploadAsset.UploadAssetResponse"/> so consumers can reuse a
/// single client-side type.
/// </summary>
public sealed record GetAssetMetadataResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    AssetVisibility Visibility,
    AssetPurpose Purpose,
    DateTimeOffset CreatedAt);
