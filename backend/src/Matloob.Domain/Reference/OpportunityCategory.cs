using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

/// <summary>
/// Opportunity category lookup. Hierarchical (parent / children) and carries
/// two boolean flags that drive init-data filtering:
///   - <see cref="ForVacancy"/>: shown in the individuals (worker) flow.
///   - <see cref="IsOther"/>: the "Other" catch-all category; excluded from
///     the individuals' filterable category list.
/// Matches the legacy <c>opportunity_categories</c> table 1:1 except that
/// the legacy bigint <c>parent_id</c> becomes a UUID FK.
/// </summary>
public sealed class OpportunityCategory : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid? ParentId { get; private set; }
    public OpportunityCategory? Parent { get; private set; }

    private readonly List<OpportunityCategory> _children = new();
    public IReadOnlyCollection<OpportunityCategory> Children => _children;

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Icon { get; private set; }
    public bool ForVacancy { get; private set; }
    public bool IsOther { get; private set; }
    public bool IsActive { get; private set; } = true;

    private OpportunityCategory() { }

    public OpportunityCategory(
        Guid id,
        string title,
        Guid? parentId = null,
        string? description = null,
        string? icon = null,
        bool forVacancy = false,
        bool isOther = false,
        bool isActive = true)
    {
        Id = id;
        Title = title;
        ParentId = parentId;
        Description = description;
        Icon = icon;
        ForVacancy = forVacancy;
        IsOther = isOther;
        IsActive = isActive;
    }
}
