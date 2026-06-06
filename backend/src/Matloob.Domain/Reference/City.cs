using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

/// <summary>
/// City lookup. Legacy: <c>cities</c> table, columns <c>id, uuid, name</c>.
/// New: single canonical name field; admins toggle <see cref="IsActive"/> to
/// hide a value without losing FK references.
/// </summary>
public sealed class City : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>Owning region (Saudi hierarchy: Region → City → District).
    /// Nullable so legacy/unmapped cities stay valid until backfilled.</summary>
    public Guid? RegionId { get; private set; }

    public bool IsActive { get; private set; } = true;

    private City() { }

    public City(Guid id, string name, bool isActive = true)
    {
        Id = id;
        Name = name;
        IsActive = isActive;
    }

    public City(Guid id, string name, Guid? regionId, bool isActive = true)
    {
        Id = id;
        Name = name;
        RegionId = regionId;
        IsActive = isActive;
    }

    /// <summary>Link this city to its region (used by the geography backfill).</summary>
    public void SetRegion(Guid regionId) => RegionId = regionId;
}
