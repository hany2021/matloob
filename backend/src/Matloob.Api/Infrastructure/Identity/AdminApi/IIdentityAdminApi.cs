namespace Matloob.Api.Infrastructure.Identity.AdminApi;

/// <summary>
/// Wrapper around the NEC IdentityServer v2 STS Identity management API.
/// Direct port of the legacy Laravel <c>IdentityServerAdminApi</c> service:
/// auth via <c>x-api-key</c>, all identifiers accept an email OR an IdM id
/// (the IdM controller treats them interchangeably), and every non-2xx throws
/// <see cref="IdentityAdminApiException"/>.
/// </summary>
public interface IIdentityAdminApi
{
    /// <summary>Whether the API has connection config (base URL + key).</summary>
    bool IsConfigured { get; }

    /// <summary>GetUserDetailsById. Returns null when the user does not exist (404 or null body).</summary>
    Task<IdmUser?> FindUserAsync(string emailOrId, CancellationToken ct);

    /// <summary>CreateUser. Returns the created identity (with its new id).</summary>
    Task<IdmUser> CreateUserAsync(CreateIdmUserPayload payload, CancellationToken ct);

    /// <summary>UpdateUserRoles — apply a {RolesToAdd, RolesToRemove} delta.</summary>
    Task UpdateUserRolesAsync(string emailOrId, IReadOnlyList<string> rolesToAdd, IReadOnlyList<string> rolesToRemove, CancellationToken ct);

    /// <summary>DeactivateUser — used before deleting an admin locally.</summary>
    Task DeactivateUserAsync(string emailOrId, CancellationToken ct);

    /// <summary>DeleteUser — compensator for a failed local insert after CreateUser.</summary>
    Task DeleteUserAsync(string emailOrId, CancellationToken ct);
}

/// <summary>Body for CreateUser. Mirrors the legacy payload shape.</summary>
public sealed record CreateIdmUserPayload(
    string Email,
    string UserName,
    string Name,
    IReadOnlyList<string> Roles,
    string? Password = null,
    bool IsActiveDirectory = false,
    string? SamAccountName = null);
