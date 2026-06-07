using Matloob.Domain.Users;

namespace Matloob.Domain.Admins;

/// <summary>
/// A back-office admin user. Modelled as a TPH subtype of <see cref="User"/>:
/// admins and users share the <c>users</c> table, distinguished by the
/// <c>user_type</c> discriminator (<c>"Admin"</c> vs <c>"User"</c>). An admin
/// carries no fields beyond a normal user — <see cref="User.IdentityId"/>,
/// <see cref="User.Email"/>, <see cref="User.Name"/> and
/// <see cref="User.IsActive"/> all live on the base — so this type is purely a
/// role marker. Admin-ness for authorization still comes from the IdM
/// <c>matloob_admin</c> role on the JWT, not from this row.
///
/// Created via the back-office "create admin" flow (which also provisions the
/// IdM identity + <c>matloob_admin</c> role). Public users that sign in to
/// Matloob are provisioned as the base <c>User</c> ("User") by the sync service.
/// </summary>
public sealed class Admin : User
{
    private Admin() { }

    public Admin(Guid id, string identityId, string? email, string? name)
        : base(id, identityId, email, name)
    {
    }
}
