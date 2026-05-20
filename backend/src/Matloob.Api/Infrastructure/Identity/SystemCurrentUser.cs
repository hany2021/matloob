namespace Matloob.Api.Infrastructure.Identity;

/// <summary>
/// Placeholder implementation used until JWT auth lands in Phase 4. Every
/// audited write attributes its CreatedBy/UpdatedBy/DeletedBy to "system".
/// </summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public string UserId => "system";

    public bool IsAuthenticated => false;
}
