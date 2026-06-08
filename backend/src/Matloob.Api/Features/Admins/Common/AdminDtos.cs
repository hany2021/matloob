using Matloob.Domain.Admins;

namespace Matloob.Api.Features.Admins.Common;

/// <summary>Row shape for the admin list/search screen.</summary>
public sealed record AdminListItem(
    Guid Id,
    string Name,
    string Email,
    bool IsActive,
    string? IdentityId);

/// <summary>Full admin record for the view/edit screen.</summary>
public sealed record AdminDetail(
    Guid Id,
    string Name,
    string Email,
    bool IsActive,
    string? IdentityId)
{
    public static AdminDetail From(Admin a) =>
        new(a.Id, a.Name ?? string.Empty, a.Email ?? string.Empty, a.IsActive, a.IdentityId);
}

/// <summary>Result of the "Check IdM" lookup the create form runs on the email.</summary>
public sealed record CheckIdmResult(
    bool Found,
    string? Id,
    string? Email,
    string? Name,
    IReadOnlyList<string> Roles,
    bool AlreadyAdmin);
