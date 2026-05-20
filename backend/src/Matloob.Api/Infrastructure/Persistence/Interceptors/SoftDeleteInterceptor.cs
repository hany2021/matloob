using Matloob.Api.Infrastructure.Identity;
using Matloob.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Matloob.Api.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Rewrites <see cref="EntityState.Deleted"/> on <see cref="ISoftDeletable"/>
/// entries into a soft-delete update: sets IsDeleted=true, DeletedAt=now,
/// DeletedBy=currentUser. The audit interceptor then runs and treats this as
/// a normal Modified entry.
///
/// Hard-delete is still available via <c>.IgnoreQueryFilters()</c> +
/// <c>ExecuteDeleteAsync</c>, which bypasses the change tracker.
/// </summary>
internal sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public SoftDeleteInterceptor(ICurrentUser currentUser, TimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Rewrite(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Rewrite(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Rewrite(DbContext? context)
    {
        if (context is null) return;

        var now = _clock.GetUtcNow();
        var who = _currentUser.UserId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Deleted) continue;
            if (entry.Entity is not ISoftDeletable) continue;

            entry.State = EntityState.Modified;
            SetProperty(entry, "IsDeleted", true);
            SetProperty(entry, "DeletedAt", now);
            SetProperty(entry, "DeletedBy", who);
        }
    }

    private static void SetProperty(EntityEntry entry, string propertyName, object? value)
    {
        var prop = entry.Metadata.FindProperty(propertyName);
        if (prop is null) return;
        entry.Property(propertyName).CurrentValue = value;
    }
}
