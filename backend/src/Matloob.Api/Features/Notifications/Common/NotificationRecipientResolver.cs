using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Notifications;
using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Features.Notifications.Common;

/// <summary>
/// Resolves the notification recipient for the shared user/establishment
/// notification routes: the <c>/api/establishments/*</c> variant resolves the
/// acting establishment (context + membership), the <c>/api/users/*</c> variant
/// is the current user's IdM <c>sub</c>. Returns <c>null</c> when establishment
/// resolution failed and a response was already written.
/// </summary>
internal static class NotificationRecipientResolver
{
    public static async Task<(NotificationRecipientType Type, string Id)?> ResolveAsync(
        AppDbContext db, HttpContext http, ICurrentUser currentUser, CancellationToken ct)
    {
        var path = http.Request.Path.Value ?? string.Empty;
        if (path.Contains("/establishments/", StringComparison.OrdinalIgnoreCase))
        {
            var establishmentId = await EstablishmentResourceGuards
                .ResolveForReadAsync(db, http, currentUser.UserId, ct);
            if (establishmentId is null) return null;
            return (NotificationRecipientType.Establishment, establishmentId.Value.ToString());
        }

        return (NotificationRecipientType.User, currentUser.UserId);
    }
}
