namespace Matloob.Domain.Common;

/// <summary>
/// Base class for business entities. Carries the standard audit columns and
/// soft-delete state. Populated by interceptors at <see cref="DbContext.SaveChangesAsync"/>
/// time — domain code should not assign these by hand outside of test fixtures.
///
/// Setters are <c>internal</c> so the persistence layer (interceptors,
/// configurations, value converters) can write to them while domain code goes
/// through behavior methods.
/// </summary>
/// <typeparam name="TId">The CLR type of the primary key (usually <see cref="Guid"/>).</typeparam>
public abstract class BaseAuditableEntity<TId> : BaseEntity<TId>, IAuditable, ISoftDeletable
    where TId : notnull
{
    public DateTimeOffset CreatedAt { get; internal set; }
    public string? CreatedBy { get; internal set; }

    public DateTimeOffset? UpdatedAt { get; internal set; }
    public string? UpdatedBy { get; internal set; }

    public DateTimeOffset? DeletedAt { get; internal set; }
    public string? DeletedBy { get; internal set; }
    public bool IsDeleted { get; internal set; }
}
