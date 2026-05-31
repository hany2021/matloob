using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// One certificate entry for a <see cref="User"/>. Maps the legacy
/// <c>user_certificates</c> table.
/// </summary>
public sealed class UserCertificate : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? IssuedBy { get; private set; }
    public DateOnly? IssuedAt { get; private set; }

    /// <summary>Optional certificate scan stored via the Asset GUID flow.</summary>
    public Guid? CopyAssetId { get; private set; }

    private UserCertificate() { }

    public UserCertificate(
        Guid id,
        Guid userId,
        string name,
        string? issuedBy,
        DateOnly? issuedAt,
        Guid? copyAssetId = null)
    {
        Id = id;
        UserId = userId;
        Name = name;
        IssuedBy = issuedBy;
        IssuedAt = issuedAt;
        CopyAssetId = copyAssetId;
    }

    public void Update(string name, string? issuedBy, DateOnly? issuedAt)
    {
        Name = name;
        IssuedBy = issuedBy;
        IssuedAt = issuedAt;
    }

    public void SetCopy(Guid? assetId) => CopyAssetId = assetId;
}
