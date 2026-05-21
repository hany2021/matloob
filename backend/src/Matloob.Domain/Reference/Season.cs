using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

/// <summary>
/// Season lookup. The legacy schema stored a relative image path which was
/// served via <c>Storage::temporaryUrl(...)</c>. In the new system the image
/// will be moved behind the local Assets API (Phase 6); for now we carry the
/// raw path as-is and let the init-data response return it unchanged.
///
/// The opportunities_count field in the legacy resource is intentionally
/// omitted from the entity — it will be computed via projection once the
/// Event / Opportunity entities exist (Phase 11).
/// </summary>
public sealed class Season : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string? Image { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Season() { }

    public Season(Guid id, string name, string? image = null, string? description = null, bool isActive = true)
    {
        Id = id;
        Name = name;
        Image = image;
        Description = description;
        IsActive = isActive;
    }
}
