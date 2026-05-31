using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// One work-experience entry for a <see cref="User"/>. Maps the legacy
/// <c>user_experiences</c> table.
/// </summary>
public sealed class UserExperience : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public string Company { get; private set; } = string.Empty;
    public string Position { get; private set; } = string.Empty;
    public DateOnly From { get; private set; }

    /// <summary>Null while <see cref="Current"/> is true.</summary>
    public DateOnly? To { get; private set; }
    public bool Current { get; private set; }
    public string? Description { get; private set; }
    public ExperienceType Type { get; private set; }

    private UserExperience() { }

    public UserExperience(
        Guid id,
        Guid userId,
        string company,
        string position,
        DateOnly from,
        DateOnly? to,
        bool current,
        string? description,
        ExperienceType type)
    {
        Id = id;
        UserId = userId;
        Company = company;
        Position = position;
        From = from;
        To = to;
        Current = current;
        Description = description;
        Type = type;
    }

    public void Update(
        string company,
        string position,
        DateOnly from,
        DateOnly? to,
        bool current,
        string? description,
        ExperienceType type)
    {
        Company = company;
        Position = position;
        From = from;
        To = to;
        Current = current;
        Description = description;
        Type = type;
    }
}
