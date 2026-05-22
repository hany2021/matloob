namespace Matloob.Domain.Opportunities;

/// <summary>
/// Lifecycle of an opportunity, mirroring the legacy Laravel
/// <c>OpportunityStatus</c> enum.
///
/// <para>
/// <b>Drafted</b> exists as a slot in the enum so the migration plan can
/// import legacy <c>DRAFTED</c> rows verbatim, but new opportunities created
/// through the API never start as Drafted — they jump straight to
/// <see cref="Upcoming"/> or <see cref="Active"/> based on start_date. See
/// Q-OPP-1 in docs/40-api-migration-readiness.md.
/// </para>
/// </summary>
public enum OpportunityStatus
{
    /// <summary>Imported legacy state; new opportunities do not start here (Q-OPP-1).</summary>
    Drafted = 0,

    /// <summary>Published, event start_date in the future; applications open.</summary>
    Upcoming = 1,

    /// <summary>Event is running; applications still accepted until personnel is met.</summary>
    Active = 2,

    /// <summary>Owner ended the opportunity manually (PATCH /me/opportunities/{id}/end).</summary>
    Ended = 3,

    /// <summary>Event end_date passed without explicit end — derived/eventual state.</summary>
    Finished = 4,
}
