namespace Matloob.Domain.Common;

/// <summary>
/// Minimal identity carrier for any persisted entity. Holds nothing but an Id.
/// Most business entities inherit <see cref="BaseAuditableEntity{TId}"/> instead;
/// this base exists for the rare entity that needs an identity column but no
/// audit / soft-delete columns (e.g. truly append-only logs).
/// </summary>
/// <typeparam name="TId">The CLR type of the primary key (usually <see cref="Guid"/>).</typeparam>
public abstract class BaseEntity<TId>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;
}
