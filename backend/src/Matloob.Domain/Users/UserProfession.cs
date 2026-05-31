using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// A user's professional interest — a link to a child
/// <see cref="Reference.OpportunityCategory"/> (one with a parent). Maps the
/// legacy <c>user_profession</c> pivot, including its <c>other</c> free-text
/// column used when the category is the "other" sentinel.
/// </summary>
public sealed class UserProfession : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public Guid OpportunityCategoryId { get; private set; }

    /// <summary>Free text, only set when the linked category is <c>is_other</c>.</summary>
    public string? Other { get; private set; }

    private UserProfession() { }

    public UserProfession(Guid id, Guid userId, Guid opportunityCategoryId, string? other)
    {
        Id = id;
        UserId = userId;
        OpportunityCategoryId = opportunityCategoryId;
        Other = other;
    }

    public void SetOther(string? other) => Other = other;
}
