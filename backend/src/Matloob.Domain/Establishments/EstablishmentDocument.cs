using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Binds one document slot on an <see cref="Establishment"/> to one
/// <see cref="Matloob.Domain.Assets.Asset"/>. Two slots exist today:
/// AuthorizationLetter and CommercialRegistration
/// (docs/15-establishment-onboarding-spec.md §5).
///
/// One active row per (EstablishmentId, DocumentType) is enforced by a
/// partial unique index. Re-uploading the same document type soft-deletes
/// the existing row and inserts a new one — both this row and the underlying
/// Asset cascade through soft-delete.
/// </summary>
public sealed class EstablishmentDocument : BaseAuditableEntity<Guid>
{
    public Guid EstablishmentId { get; private set; }
    public EstablishmentDocumentType DocumentType { get; private set; }

    /// <summary>FK to the Assets table.</summary>
    public Guid AssetId { get; private set; }

    /// <summary>Sub claim of the uploader. Audit-only; not used for download authorization.</summary>
    public string UploadedByUserId { get; private set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; private set; }

    private EstablishmentDocument() { }

    public EstablishmentDocument(
        Guid id,
        Guid establishmentId,
        EstablishmentDocumentType documentType,
        Guid assetId,
        string uploadedByUserId,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        EstablishmentId = establishmentId;
        DocumentType = documentType;
        AssetId = assetId;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = uploadedAt;
    }
}
