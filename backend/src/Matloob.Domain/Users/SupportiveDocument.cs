using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// A user-supplied supporting document — either an uploaded file (Asset GUID)
/// or an external URL. Maps the legacy <c>supportive_documents</c> table.
/// </summary>
public sealed class SupportiveDocument : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>External link. Mutually exclusive with <see cref="FileAssetId"/>.</summary>
    public string? Url { get; private set; }

    /// <summary>Uploaded file via the Asset GUID flow. Clears <see cref="Url"/>.</summary>
    public Guid? FileAssetId { get; private set; }

    private SupportiveDocument() { }

    public SupportiveDocument(
        Guid id,
        Guid userId,
        string name,
        string? url = null,
        Guid? fileAssetId = null)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Url = url;
        FileAssetId = fileAssetId;
    }

    public void Rename(string name) => Name = name;

    /// <summary>Attach a file; per legacy semantics this nulls the URL.</summary>
    public void SetFile(Guid assetId)
    {
        FileAssetId = assetId;
        Url = null;
    }

    /// <summary>Set an external URL; clears any attached file.</summary>
    public void SetUrl(string url)
    {
        Url = url;
        FileAssetId = null;
    }
}
