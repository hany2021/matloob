using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// A portfolio entry on an establishment's public profile (legacy
/// <c>establishment_experiences</c>): "we ran event/opportunity X from..to".
/// The legacy <c>category</c> morph (EventType | OpportunityCategory) is reduced
/// to <see cref="CategoryId"/> + <see cref="Type"/> — the type discriminates
/// which reference table the id points at. Created via the profile
/// "Add experience" dialog; <see cref="JobTitle"/> is fixed (the legacy service
/// hard-set it).
/// </summary>
public sealed class EstablishmentExperience : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid EstablishmentId { get; private set; }
    public EstablishmentExperienceType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string JobTitle { get; private set; } = string.Empty;

    /// <summary>Event type id (Type=Event) or opportunity category id (Type=Opportunity).</summary>
    public Guid CategoryId { get; private set; }

    public string Description { get; private set; } = string.Empty;
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }

    private EstablishmentExperience() { }

    public EstablishmentExperience(
        Guid id,
        Guid establishmentId,
        EstablishmentExperienceType type,
        string name,
        string jobTitle,
        Guid categoryId,
        string description,
        DateOnly from,
        DateOnly to)
    {
        Id = id;
        EstablishmentId = establishmentId;
        Type = type;
        Name = name.Trim();
        JobTitle = jobTitle.Trim();
        CategoryId = categoryId;
        Description = description.Trim();
        From = from;
        To = to;
    }
}
