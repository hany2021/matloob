using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Establishment-provided service. Mirrors the legacy Laravel <c>services</c>
/// table (id/uuid, establishment_id, name, description). A pure profile-
/// enrichment entity — no Ajeer/contract coupling. Owned by an
/// <see cref="Establishment"/> via <see cref="EstablishmentId"/>.
/// </summary>
public sealed class Service : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid EstablishmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    private Service() { }

    public Service(Guid id, Guid establishmentId, string name, string description)
    {
        Id = id;
        EstablishmentId = establishmentId;
        Name = Normalize(name);
        Description = Normalize(description);
    }

    public void Update(string name, string description)
    {
        Name = Normalize(name);
        Description = Normalize(description);
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
