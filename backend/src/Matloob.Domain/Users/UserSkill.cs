using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// One skill entry for a <see cref="User"/>. Maps the legacy <c>skills</c>
/// table (which carried a <c>user_id</c> FK).
/// </summary>
public sealed class UserSkill : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ProficiencyLevel Level { get; private set; }

    private UserSkill() { }

    public UserSkill(Guid id, Guid userId, string name, ProficiencyLevel level)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Level = level;
    }

    public void Update(string name, ProficiencyLevel level)
    {
        Name = name;
        Level = level;
    }
}
