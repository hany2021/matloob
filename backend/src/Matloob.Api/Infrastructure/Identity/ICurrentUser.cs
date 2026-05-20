namespace Matloob.Api.Infrastructure.Identity;

/// <summary>
/// Abstraction over "who is making this request right now."  Read by the
/// auditing interceptor to populate CreatedBy / UpdatedBy / DeletedBy.
///
/// Until JWT authentication arrives in Phase 4 the default implementation
/// returns a fixed "system" id; once JWT is wired the production binding will
/// read <c>sub</c> from the validated bearer token.
/// </summary>
public interface ICurrentUser
{
    string UserId { get; }

    bool IsAuthenticated { get; }
}
