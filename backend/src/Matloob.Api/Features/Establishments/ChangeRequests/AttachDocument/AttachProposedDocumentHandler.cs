using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Assets;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.AttachDocument;

/// <summary>
/// Shared orchestration for the two proposed-document endpoints. The
/// pattern matches <c>LinkDocumentHandler</c> from onboarding except that
/// the asset id lands on the ChangeRequest (ProposedXxxAssetId) instead of
/// inserting a live <see cref="EstablishmentDocument"/> row. Live documents
/// stay intact until the change request is approved.
/// </summary>
public sealed class AttachProposedDocumentHandler
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AttachProposedDocumentHandler(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public enum Outcome
    {
        Attached,
        EstablishmentNotFound,
        EstablishmentSuspended,
        ChangeRequestNotFound,
        Forbidden,
        ChangeRequestNotEditable,
        AssetNotFound,
        AssetNotOwnedByCaller,
        AssetPurposeMismatch,
    }

    public sealed record Result(
        Outcome Outcome,
        EstablishmentChangeRequest? ChangeRequest = null,
        string? ErrorCode = null);

    public async Task<Result> AttachAsync(
        Guid establishmentId,
        Guid changeRequestId,
        Guid assetId,
        EstablishmentDocumentType documentType,
        bool isAdmin,
        CancellationToken ct)
    {
        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == establishmentId, ct);
        if (establishment is null)
        {
            return new Result(Outcome.EstablishmentNotFound);
        }

        if (establishment.Status == EstablishmentStatus.Suspended)
        {
            return new Result(Outcome.EstablishmentSuspended,
                ErrorCode: EstablishmentErrorCodes.EstablishmentSuspended);
        }

        var cr = await _db.EstablishmentChangeRequests
            .FirstOrDefaultAsync(c =>
                c.Id == changeRequestId && c.EstablishmentId == establishmentId, ct);
        if (cr is null)
        {
            return new Result(Outcome.ChangeRequestNotFound);
        }

        if (!isAdmin)
        {
            var isOwner = await MembershipChecks.HasPermissionAsync(
                _db, establishmentId, _currentUser.UserId, Infrastructure.Auth.Permissions.ChangeRequests.Submit, ct);
            if (!isOwner)
            {
                return new Result(Outcome.Forbidden);
            }
        }

        if (!cr.IsEditableByOwner)
        {
            return new Result(Outcome.ChangeRequestNotEditable,
                ErrorCode: EstablishmentErrorCodes.CannotEditInStatus);
        }

        var asset = await _db.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset is null)
        {
            return new Result(Outcome.AssetNotFound,
                ErrorCode: EstablishmentErrorCodes.AssetNotFound);
        }

        // Admin override: admins can attach an asset they don't own
        // (support flow). Owners can only attach their own assets.
        if (!isAdmin &&
            !(asset.OwnerUserId is { Length: > 0 } owner &&
              string.Equals(owner, _currentUser.UserId, StringComparison.Ordinal)))
        {
            return new Result(Outcome.AssetNotOwnedByCaller,
                ErrorCode: EstablishmentErrorCodes.AssetNotOwnedByCaller);
        }

        var requiredPurpose = documentType switch
        {
            EstablishmentDocumentType.AuthorizationLetter => AssetPurpose.AuthorizationLetter,
            EstablishmentDocumentType.CommercialRegistration => AssetPurpose.CommercialRegistration,
            _ => throw new ArgumentOutOfRangeException(nameof(documentType)),
        };
        if (asset.Purpose != requiredPurpose)
        {
            return new Result(Outcome.AssetPurposeMismatch,
                ErrorCode: EstablishmentErrorCodes.AssetPurposeMismatch);
        }

        // Stash the asset id on the change request. The live document slot
        // stays intact -- swap-on-approval happens in the approve endpoint.
        if (documentType == EstablishmentDocumentType.AuthorizationLetter)
        {
            cr.AttachAuthorizationLetter(assetId);
        }
        else
        {
            cr.AttachCommercialRegistration(assetId);
        }

        await _db.SaveChangesAsync(ct);

        return new Result(Outcome.Attached, ChangeRequest: cr);
    }
}
