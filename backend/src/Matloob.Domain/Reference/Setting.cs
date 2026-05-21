using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

/// <summary>
/// Key/value app setting. Legacy <c>settings</c> table.
///
/// The value is stored as <c>text</c> and parsed on read by the init-data
/// endpoint (the legacy controller did the same: it sniffed for "true"/"false",
/// numerics, and JSON). No type column — we trust seeders + admin tools to
/// store sensible content for each key.
/// </summary>
public sealed class Setting : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Key { get; private set; } = string.Empty;
    public string? Value { get; private set; }

    /// <summary>
    /// Mirrors the legacy <c>active</c> column. The init-data response omits
    /// inactive settings; admin UI can still list them.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    private Setting() { }

    public Setting(Guid id, string key, string? value, bool isActive = true)
    {
        Id = id;
        Key = key;
        Value = value;
        IsActive = isActive;
    }
}
