using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

public sealed class Bank : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private Bank() { }

    public Bank(Guid id, string name, bool isActive = true)
    {
        Id = id;
        Name = name;
        IsActive = isActive;
    }
}
