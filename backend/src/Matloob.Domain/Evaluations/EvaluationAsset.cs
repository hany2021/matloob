using Matloob.Domain.Common;

namespace Matloob.Domain.Evaluations;

/// <summary>
/// Join row binding one <see cref="Evaluation"/> to one
/// <see cref="Matloob.Domain.Assets.Asset"/>. Replaces the Spatie media
/// rows the legacy Evaluation model used for establishment-side evidence
/// uploads.
/// </summary>
public sealed class EvaluationAsset : BaseAuditableEntity<Guid>
{
    public Guid EvaluationId { get; private set; }
    public Guid AssetId { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    private EvaluationAsset() { }

    public EvaluationAsset(
        Guid id,
        Guid evaluationId,
        Guid assetId,
        string uploadedByUserId,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        EvaluationId = evaluationId;
        AssetId = assetId;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = uploadedAt;
    }
}
