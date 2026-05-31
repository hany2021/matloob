using Matloob.Domain.Common;

namespace Matloob.Domain.Assets;

/// <summary>
/// Polymorphic attachment link — the new system's equivalent of the legacy
/// Spatie <c>media</c> table, decoupled from the consuming aggregates. Binds a
/// canonical <see cref="Asset"/> (the blob + metadata) to any owner via
/// <see cref="ModelType"/> + <see cref="ModelId"/>, grouped by
/// <see cref="CollectionName"/> (e.g. <c>uploads</c>, <c>photo</c>) and ordered
/// by <see cref="OrderColumn"/>. Any entity can attach files without its own
/// join table.
/// </summary>
public sealed class Media : BaseAuditableEntity<Guid>
{
    /// <summary>The blob this row points at.</summary>
    public Guid AssetId { get; private set; }

    /// <summary>Owner kind, e.g. <c>Event</c>, <c>Opportunity</c>, <c>SuccessManagementCriterion</c>.</summary>
    public string ModelType { get; private set; } = string.Empty;

    /// <summary>Owner key — a Guid (string) for most aggregates, or the IdM sub for users.</summary>
    public string ModelId { get; private set; } = string.Empty;

    /// <summary>Collection grouping within the owner (legacy <c>collection_name</c>).</summary>
    public string CollectionName { get; private set; } = string.Empty;

    public int OrderColumn { get; private set; }

    public string? UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private Media() { }

    public Media(
        Guid id,
        Guid assetId,
        string modelType,
        string modelId,
        string collectionName,
        int orderColumn,
        string? uploadedByUserId,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        AssetId = assetId;
        ModelType = modelType;
        ModelId = modelId;
        CollectionName = collectionName;
        OrderColumn = orderColumn;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = uploadedAt;
    }
}
