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
    /// <summary>The IdM <c>sub</c> (identity id) read from the JWT.</summary>
    string UserId { get; }

    /// <summary>
    /// The local Matloob <c>users.id</c> for the current request. Null until set
    /// by <c>CurrentUserMiddleware</c> (which runs after the user-sync middleware
    /// provisions the row), and for unauthenticated / system contexts.
    /// </summary>
    Guid? MatloobUserId { get; }

    bool IsAuthenticated { get; }

    /// <summary>Set the resolved local user id for this request. Called once per
    /// request by the middleware; not for general use.</summary>
    void SetMatloobUserId(Guid matloobUserId);
}
