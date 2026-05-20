namespace Matloob.Domain.Common;

/// <summary>
/// Marker interface for entities that carry the standard audit columns
/// (CreatedAt/By, UpdatedAt/By). Read by the auditing interceptor; not
/// directly implemented by domain code — derive from
/// <see cref="BaseAuditableEntity{TId}"/> instead.
/// </summary>
public interface IAuditable
{
}
