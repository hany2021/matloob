using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// A user's proficiency in a reference <see cref="Reference.Language"/>. Maps
/// the legacy <c>language_user</c> pivot (which carried a <c>level</c> column).
/// Modeled with a surrogate Guid key + a unique (UserId, LanguageId) index to
/// match the codebase convention rather than a composite PK.
/// </summary>
public sealed class UserLanguageProficiency : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public Guid LanguageId { get; private set; }
    public ProficiencyLevel Level { get; private set; }

    private UserLanguageProficiency() { }

    public UserLanguageProficiency(Guid id, Guid userId, Guid languageId, ProficiencyLevel level)
    {
        Id = id;
        UserId = userId;
        LanguageId = languageId;
        Level = level;
    }

    public void SetLevel(ProficiencyLevel level) => Level = level;
}
