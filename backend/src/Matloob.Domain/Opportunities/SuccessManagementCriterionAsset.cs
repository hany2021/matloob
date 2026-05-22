using Matloob.Domain.Common;

namespace Matloob.Domain.Opportunities;

/// <summary>
/// Join row binding one <see cref="SuccessManagementCriterion"/> to one
/// <see cref="Matloob.Domain.Assets.Asset"/>. Replaces the Spatie media
/// rows used on the legacy model.
/// </summary>
public sealed class SuccessManagementCriterionAsset : BaseAuditableEntity<Guid>
{
    public Guid SuccessManagementCriterionId { get; private set; }
    public Guid AssetId { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    private SuccessManagementCriterionAsset() { }

    public SuccessManagementCriterionAsset(
        Guid id,
        Guid successManagementCriterionId,
        Guid assetId,
        string uploadedByUserId,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        SuccessManagementCriterionId = successManagementCriterionId;
        AssetId = assetId;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = uploadedAt;
    }
}
