using Matloob.Domain.Common;

namespace Matloob.Domain.Opportunities;

/// <summary>
/// Join row binding one <see cref="Opportunity"/> to one
/// <see cref="Matloob.Domain.Assets.Asset"/>. Mirrors the Spatie media
/// rows used on the legacy Opportunity model; the new system uses the
/// canonical Assets API instead.
///
/// <para>
/// Inherits <see cref="BaseAuditableEntity{TId}"/> so soft-delete cascades
/// with the parent Opportunity by interceptor convention (the asset itself
/// is reference-counted; physical deletion happens through the asset
/// cleanup job).
/// </para>
/// </summary>
public sealed class OpportunityAsset : BaseAuditableEntity<Guid>
{
    public Guid OpportunityId { get; private set; }
    public Guid AssetId { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    private OpportunityAsset() { }

    public OpportunityAsset(
        Guid id,
        Guid opportunityId,
        Guid assetId,
        string uploadedByUserId,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        OpportunityId = opportunityId;
        AssetId = assetId;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = uploadedAt;
    }
}
