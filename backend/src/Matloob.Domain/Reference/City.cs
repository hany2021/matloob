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
    public bool IsActive { get; private set; } = true;

    private City() { }

    public City(Guid id, string name, bool isActive = true)
    {
        Id = id;
        Name = name;
        IsActive = isActive;
    }
}
