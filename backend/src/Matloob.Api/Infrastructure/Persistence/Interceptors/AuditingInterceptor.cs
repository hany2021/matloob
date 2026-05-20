using Matloob.Api.Infrastructure.Identity;
using Matloob.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Matloob.Api.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Populates CreatedAt/By + UpdatedAt/By on entities derived from
/// <see cref="BaseAuditableEntity{TId}"/>.  Runs immediately before the
/// DbContext commits its change tracker to the database.
///
/// Delete-state mutations are handled by <see cref="SoftDeleteInterceptor"/>
/// running just before this; by the time this interceptor sees a soft-delete
/// the entry has already been rewritten to Modified with IsDeleted=true and
/// DeletedAt/By populated, so the audit pass treats it as a regular update.
/// </summary>
internal sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AuditingInterceptor(ICurrentUser currentUser, TimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        var now = _clock.GetUtcNow();
        var who = _currentUser.UserId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IAuditable) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    SetIfHasProperty(entry, "CreatedAt", now);
                    SetIfHasProperty(entry, "CreatedBy", who);
                    break;
                case EntityState.Modified:
                    SetIfHasProperty(entry, "UpdatedAt", now);
                    SetIfHasProperty(entry, "UpdatedBy", who);
                    break;
            }
        }
    }

    private static void SetIfHasProperty(EntityEntry entry, string propertyName, object? value)
    {
        var prop = entry.Metadata.FindProperty(propertyName);
        if (prop is null) return;
        entry.Property(propertyName).CurrentValue = value;
    }
}
