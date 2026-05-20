namespace Matloob.Domain.Common;

/// <summary>
/// Marker + state contract for entities that participate in soft-delete.
/// Implementing this interface opts the entity into:
///   - the AppDbContext global query filter that hides <c>IsDeleted == true</c> rows,
///   - the SoftDeleteInterceptor that rewrites <c>EntityState.Deleted</c> into an
///     update setting <c>IsDeleted, DeletedAt, DeletedBy</c>.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    string? DeletedBy { get; }
}
