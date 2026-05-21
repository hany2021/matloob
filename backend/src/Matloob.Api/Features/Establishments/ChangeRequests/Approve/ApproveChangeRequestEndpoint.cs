using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.Approve;

/// <summary>
/// <c>POST /api/v1/admin/establishments/change-requests/{id}/approve</c> —
/// admin transitions PendingReview → Approved and applies the proposed
/// changes to the live establishment (spec §7.3).
///
/// Apply order:
///   1. Scalar fields: <see cref="Establishment.ApplyApprovedChangeRequest"/>
///      overwrites every non-null Proposed* on the live row.
///   2. Documents: for each non-null ProposedXxxAssetId, the active
///      <see cref="EstablishmentDocument"/> of that type is soft-deleted
///      (so is its underlying Asset; orphan-cleanup job purges bytes
///      after 30 days), and a fresh document row is inserted pointing at
///      the proposed asset.
///   3. The change request itself is flipped Approved + AppliedAt
///      stamped via <see cref="EstablishmentChangeRequest.Approve"/>.
///   4. Append an <c>EstablishmentReviewHistory</c> row, action
///      <c>ChangeRequestApproved</c>.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class ApproveChangeRequestEndpoint : EndpointWithoutRequest<ApproveChangeRequestResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ApproveChangeRequestEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Post("/api/v1/admin/establishments/change-requests/{id}/approve");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ApproveChangeRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Approve a PendingReview ChangeRequest and apply the proposed values.";
            s.Description =
                "Applies non-null Proposed* fields to the live Establishment and " +
                "swaps any proposed documents (soft-deleting the previous slot + " +
                "its Asset).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var cr = await _db.EstablishmentChangeRequests
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cr is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (cr.Status != EstablishmentChangeRequestStatus.PendingReview)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot approve in status '{cr.Status}'. Allowed: PendingReview.",
                ct);
            return;
        }

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == cr.EstablishmentId, ct);
        if (establishment is null)
        {
            // Orphan -- treat as 404 from the admin perspective.
            await Send.NotFoundAsync(ct);
            return;
        }

        if (establishment.Status != EstablishmentStatus.Approved)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Live establishment must be Approved to apply a ChangeRequest. Current: {establishment.Status}.",
                ct);
            return;
        }

        // 1. Apply scalar fields.
        establishment.ApplyApprovedChangeRequest(cr);

        var now = _clock.GetUtcNow();

        // 2. Swap documents for any proposed-asset slot.
        if (cr.ProposedAuthorizationLetterAssetId is { } authAssetId)
        {
            await SwapDocumentAsync(
                establishment.Id,
                EstablishmentDocumentType.AuthorizationLetter,
                authAssetId,
                now,
                ct);
        }
        if (cr.ProposedCommercialRegistrationAssetId is { } crAssetId)
        {
            await SwapDocumentAsync(
                establishment.Id,
                EstablishmentDocumentType.CommercialRegistration,
                crAssetId,
                now,
                ct);
        }

        // 3. Flip the change request.
        cr.Approve(now, _currentUser.UserId);

        // 4. Append audit history.
        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            action: EstablishmentReviewAction.ChangeRequestApproved,
            occurredAt: now,
            changeRequestId: cr.Id,
            actorAdminId: _currentUser.UserId));

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueConstraintTranslator.TryTranslate(ex) is { } conflict)
        {
            // Proposed CR-number could collide with another establishment's
            // live CR-number (raced by an onboarding approve), or the
            // document swap could race a parallel re-link. Either way the
            // partial unique indexes settle the conflict; surface 409 +
            // machine-readable code instead of a generic 500.
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                conflict.Code, conflict.Detail, ct);
            return;
        }

        await Send.OkAsync(
            new ApproveChangeRequestResponse(
                Id: cr.Id,
                EstablishmentId: establishment.Id,
                Status: cr.Status,
                ReviewedAt: cr.ReviewedAt!.Value,
                AppliedAt: cr.AppliedAt!.Value),
            ct);
    }

    /// <summary>
    /// Spec §7.3.2: soft-delete the current EstablishmentDocument of this
    /// type AND its underlying Asset, then insert a fresh document row
    /// pointing at the proposed asset.
    /// </summary>
    private async Task SwapDocumentAsync(
        Guid establishmentId,
        EstablishmentDocumentType documentType,
        Guid newAssetId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await _db.EstablishmentDocuments
            .FirstOrDefaultAsync(d =>
                d.EstablishmentId == establishmentId && d.DocumentType == documentType,
                ct);
        if (existing is not null)
        {
            _db.EstablishmentDocuments.Remove(existing);

            var oldAsset = await _db.Assets
                .FirstOrDefaultAsync(a => a.Id == existing.AssetId, ct);
            if (oldAsset is not null)
            {
                _db.Assets.Remove(oldAsset);
            }
        }

        _db.EstablishmentDocuments.Add(new EstablishmentDocument(
            id: Guid.NewGuid(),
            establishmentId: establishmentId,
            documentType: documentType,
            assetId: newAssetId,
            uploadedByUserId: _currentUser.UserId, // the admin who approved.
            uploadedAt: now));
    }
}

public sealed record ApproveChangeRequestResponse(
    Guid Id,
    Guid EstablishmentId,
    EstablishmentChangeRequestStatus Status,
    DateTimeOffset ReviewedAt,
    DateTimeOffset AppliedAt);
