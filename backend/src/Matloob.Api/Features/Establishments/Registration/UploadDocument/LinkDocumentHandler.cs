using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Assets;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Registration.UploadDocument;

/// <summary>
/// Shared logic for both document-link endpoints (AuthorizationLetter and
/// CommercialRegistration). Lives outside the endpoint classes because the
/// rules are identical except for the document type — extracting the
/// orchestration here keeps the endpoints near-empty and makes the rules
/// auditable in one place.
///
/// Re-link semantics (spec §5):
///   1. The new asset must already exist, be the right Purpose, and be
///      owned by the caller (or accessible to admins — owners only for
///      now since the upload path only sets <c>OwnerUserId</c>).
///   2. If an active <see cref="EstablishmentDocument"/> of this type
///      already exists, it is soft-deleted, AND the underlying old Asset
///      row is also soft-deleted. The orphan cleanup job purges the bytes
///      30 days later.
///   3. A fresh <see cref="EstablishmentDocument"/> row points at the new
///      Asset.
/// </summary>
public sealed class LinkDocumentHandler
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public LinkDocumentHandler(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Outcome of <see cref="LinkAsync"/>. The endpoint translates these into
    /// HTTP results — keeping HTTP concerns out of the handler.
    /// </summary>
    public enum Outcome
    {
        Linked,
        EstablishmentNotFound,
        Forbidden,
        EstablishmentNotEditable,
        AssetNotFound,
        AssetNotOwnedByCaller,
        AssetPurposeMismatch,
    }

    public sealed record Result(
        Outcome Outcome,
        EstablishmentDocument? Document = null,
        string? ErrorCode = null);

    public async Task<Result> LinkAsync(
        Guid establishmentId,
        Guid assetId,
        EstablishmentDocumentType documentType,
        CancellationToken ct)
    {
        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == establishmentId, ct);
        if (establishment is null)
        {
            return new Result(Outcome.EstablishmentNotFound);
        }

        if (!string.Equals(establishment.CreatedByUserId, _currentUser.UserId, StringComparison.Ordinal))
        {
            return new Result(Outcome.Forbidden);
        }

        if (!establishment.IsEditableByCreator)
        {
            return new Result(Outcome.EstablishmentNotEditable,
                ErrorCode: EstablishmentErrorCodes.CannotEditInStatus);
        }

        // Soft-delete query filter hides any already-deleted row; if we find
        // nothing it's either non-existent OR soft-deleted, either way 404.
        var asset = await _db.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset is null)
        {
            return new Result(Outcome.AssetNotFound,
                ErrorCode: EstablishmentErrorCodes.AssetNotFound);
        }

        if (!IsAssetOwnedByCurrentUser(asset))
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

        // §5 re-link: soft-delete the previous active document AND its asset.
        var existing = await _db.EstablishmentDocuments
            .FirstOrDefaultAsync(
                d => d.EstablishmentId == establishmentId && d.DocumentType == documentType,
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

        var now = _clock.GetUtcNow();
        var doc = new EstablishmentDocument(
            id: Guid.NewGuid(),
            establishmentId: establishmentId,
            documentType: documentType,
            assetId: assetId,
            uploadedByUserId: _currentUser.UserId,
            uploadedAt: now);

        _db.EstablishmentDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);

        return new Result(Outcome.Linked, Document: doc);
    }

    /// <summary>
    /// Today every asset that goes through the upload endpoint records
    /// <c>OwnerUserId</c> as the uploader's sub. Membership-based access
    /// (admin or other establishment members) lands in a later phase along
    /// with the broader AssetAccessRules update.
    /// </summary>
    private bool IsAssetOwnedByCurrentUser(Asset asset) =>
        asset.OwnerUserId is { Length: > 0 } owner &&
        string.Equals(owner, _currentUser.UserId, StringComparison.Ordinal);
}
