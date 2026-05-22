using Matloob.Domain.Common;

namespace Matloob.Domain.Applications;

/// <summary>
/// Application to an opportunity. Mirrors the legacy Laravel
/// <c>applicants</c> table — including its polymorphic <c>applier</c>
/// relation — but replaces the morph (applier_type / applier_id) with two
/// explicit FK columns, exactly one of which is set.
///
/// <para>
/// <b>Status.</b> The legacy table had no status column; the "status" of
/// an application is derived from related offer state (see Laravel
/// <c>ApplicantSupport::getApplicationStatus()</c>). The new system keeps
/// the same convention — status is a read-side projection, NOT a column.
/// </para>
///
/// <para>
/// <b>Why two FKs instead of a morph?</b> EF + Postgres handle polymorphic
/// relations poorly compared to Laravel. Split columns let us declare hard
/// FKs and keep the constraint surface explicit. A DB-level CHECK
/// constraint enforces "exactly one of (applicant_user_id,
/// applicant_establishment_id) is set."
/// </para>
/// </summary>
public sealed class OpportunityApplication : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid OpportunityId { get; private set; }

    /// <summary>
    /// IdM sub claim of the individual applicant. Non-null when the
    /// applier is a <see cref="Matloob.Domain.Users.User"/>.
    /// </summary>
    public string? ApplicantUserId { get; private set; }

    /// <summary>
    /// FK to establishments.id. Non-null when the applier is another
    /// establishment.
    /// </summary>
    public Guid? ApplicantEstablishmentId { get; private set; }

    /// <summary>
    /// IdM sub claim of the human inside an establishment who submitted
    /// the application. Always null for individual-user applications.
    /// </summary>
    public string? AppliedByUserId { get; private set; }

    private OpportunityApplication() { }

    /// <summary>
    /// Application by an individual worker.
    /// </summary>
    public static OpportunityApplication ForUser(
        Guid id,
        Guid opportunityId,
        string applicantUserId)
    {
        if (string.IsNullOrWhiteSpace(applicantUserId))
        {
            throw new ArgumentException("Applicant user id is required.", nameof(applicantUserId));
        }
        return new OpportunityApplication
        {
            Id = id,
            OpportunityId = opportunityId,
            ApplicantUserId = applicantUserId,
            ApplicantEstablishmentId = null,
            AppliedByUserId = null,
        };
    }

    /// <summary>
    /// Application by an organization. <paramref name="appliedByUserId"/>
    /// is the human inside the org who submitted it (audit only).
    /// </summary>
    public static OpportunityApplication ForEstablishment(
        Guid id,
        Guid opportunityId,
        Guid applicantEstablishmentId,
        string appliedByUserId)
    {
        if (string.IsNullOrWhiteSpace(appliedByUserId))
        {
            throw new ArgumentException("Applied-by user id is required.", nameof(appliedByUserId));
        }
        return new OpportunityApplication
        {
            Id = id,
            OpportunityId = opportunityId,
            ApplicantUserId = null,
            ApplicantEstablishmentId = applicantEstablishmentId,
            AppliedByUserId = appliedByUserId,
        };
    }
}
