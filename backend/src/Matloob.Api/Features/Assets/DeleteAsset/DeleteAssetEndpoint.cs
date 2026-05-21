using FastEndpoints;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Assets.DeleteAsset;

/// <summary>
/// <c>DELETE /api/v1/assets/{id}</c> — soft-delete the asset row.
///
/// Auth: any authenticated principal can call; per-row gating is the same
/// as the read endpoints (owner OR matloob_admin). Anonymous calls return
/// 401 — public visibility does NOT grant delete.
///
/// Behavior:
///   - Sets <c>IsDeleted = true</c> via the SoftDeleteInterceptor. The row
///     stays on disk and in the DB; the global query filter hides it from
///     subsequent reads, so download + metadata return 404.
///   - The file bytes on disk are NOT removed. A nightly cleanup job (out
///     of scope this phase; tracked in
///     docs/15-establishment-onboarding-spec.md §11) will sweep blobs whose
///     row has been soft-deleted for > 30 days.
///
/// Idempotency: calling DELETE on an already-deleted asset returns 404 —
/// it's the same response a never-existed id gets, which is what callers
/// want from "is this thing gone yet?".
/// </summary>
public sealed class DeleteAssetEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;

    public DeleteAssetEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Delete("/api/v1/assets/{id}");
        // Default policy requires authentication; per-row auth in HandleAsync.
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Assets"));
        Summary(s =>
        {
            s.Summary = "Soft-delete an asset by GUID.";
            s.Description =
                "Owner OR matloob_admin only. The file bytes stay on disk " +
                "until a nightly cleanup job removes them; subsequent reads " +
                "return 404 immediately because the row is hidden by the " +
                "soft-delete query filter.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var verdict = AssetAccessRules.CanDelete(asset, User);
        switch (verdict)
        {
            case AssetAccessRules.AccessVerdict.Unauthenticated:
                await Send.UnauthorizedAsync(ct);
                return;
            case AssetAccessRules.AccessVerdict.Forbidden:
                await Send.ForbiddenAsync(ct);
                return;
        }

        // Remove(...) -> SoftDeleteInterceptor rewrites Deleted -> Modified
        // and sets IsDeleted / DeletedAt / DeletedBy. The audit interceptor
        // then captures the change like any other Modified entry.
        _db.Assets.Remove(asset);
        await _db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
