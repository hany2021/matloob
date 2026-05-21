using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

/// <summary>
/// UI translation key. Legacy <c>translations</c> table with composite unique
/// (key, locale). The init-data response groups translations into a dict
/// keyed by <see cref="Key"/> for the requested locale (default <c>en</c>).
/// </summary>
public sealed class Translation : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Key { get; private set; } = string.Empty;

    /// <summary>BCP-47 lowercase, e.g. "en", "ar".</summary>
    public string Locale { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    private Translation() { }

    public Translation(Guid id, string key, string locale, string value, bool isActive = true)
    {
        Id = id;
        Key = key;
        Locale = locale;
        Value = value;
        IsActive = isActive;
    }
}
