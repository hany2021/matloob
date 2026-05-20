namespace Matloob.Domain.Common;

/// <summary>
/// Marker interface for aggregate roots. Use to distinguish entities that may
/// be loaded by repositories or referenced by FK from outside their aggregate,
/// from entities that are owned children of an aggregate.
/// </summary>
public interface IAggregateRoot
{
}
