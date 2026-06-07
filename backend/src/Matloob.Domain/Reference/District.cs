using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

/// <summary>
/// District (حي) lookup, owned by a <see cref="City"/>. Completes the Saudi
/// address hierarchy: Region → City → District. Admins toggle
/// <see cref="IsActive"/> to hide a value without breaking references.
/// </summary>
public sealed class District : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public Guid CityId { get; private set; }
    public bool IsActive { get; private set; } = true;

    private District() { }

    public District(Guid id, string name, Guid cityId, bool isActive = true)
    {
        Id = id;
        Name = name;
        CityId = cityId;
        IsActive = isActive;
    }
}
