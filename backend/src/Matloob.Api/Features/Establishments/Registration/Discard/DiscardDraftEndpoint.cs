using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Registration.Discard;

/// <summary>
/// <c>DELETE /api/v1/establishments/registration/{id}</c> — soft-deletes a
/// Draft establishment and its in-flight onboarding state. Spec §9.
///
/// Auth: <see cref="MatloobPolicies.User"/> + ownership
/// (CreatedByUserId == JWT sub). Non-creator → 403; missing → 404.
///
/// Status guard: <c>Status = Draft</c> only. Any other status returns
/// 409 with code <c>cannot_delete</c>:
/// - PendingReview / Approved / Rejected / Suspended all keep the row.
///   Resubmits, change requests, and other recovery paths exist for
///   those; discarding would lose history.
///
/// Behavior:
/// - Soft-deletes the <see cref="Establishment"/> row via
///   <c>SoftDeleteInterceptor</c>.
/// - Soft-deletes every <see cref="EstablishmentDocument"/> for the
///   establishment. The underlying <see cref="Matloob.Domain.Assets.Asset"/>
///   rows are NOT touched — they were uploaded via the Assets API and
///   the orphan-cleanup cron (Phase 11) will sweep blobs that no
///   document row references.
/// - Appends a <c>Rejected</c>-style audit row? No — the spec lists no
///   dedicated action for discard, so we keep the audit clean by
///   omitting it. The soft-delete timestamp on the Establishment row
///   itself is the audit trail.
/// </summary>
public sealed class DiscardDraftEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DiscardDraftEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete("/api/v1/establishments/registration/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Discard a Draft establishment.";
            s.Description =
                "Creator only. Allowed only in Status=Draft. Soft-deletes " +
                "the establishment + its document rows; uploaded Asset " +
                "blobs are reclaimed by the nightly orphan cleanup.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!string.Equals(establishment.CreatedByUserId, _currentUser.UserId, StringComparison.Ordinal))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        if (establishment.Status != EstablishmentStatus.Draft)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotDelete,
                $"Only Draft establishments can be discarded. Current: {establishment.Status}.",
                ct);
            return;
        }

        // Soft-delete the establishment AND every document row attached to
        // it. Both are ISoftDeletable so the SoftDeleteInterceptor rewrites
        // Deleted -> Modified + stamps IsDeleted / DeletedAt / DeletedBy.
        var documents = await _db.EstablishmentDocuments
            .Where(d => d.EstablishmentId == establishment.Id)
            .ToListAsync(ct);
        if (documents.Count > 0)
        {
            _db.EstablishmentDocuments.RemoveRange(documents);
        }
        _db.Establishments.Remove(establishment);

        await _db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
