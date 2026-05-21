using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

public sealed class JobTitle : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private JobTitle() { }

    public JobTitle(Guid id, string name, bool isActive = true)
    {
        Id = id;
        Name = name;
        IsActive = isActive;
    }
}
