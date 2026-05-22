using Matloob.Domain.Common;

namespace Matloob.Domain.Opportunities;

/// <summary>
/// "Success criteria" entry the establishment sets when publishing an
/// opportunity. Legacy Laravel stored this as a polymorphic
/// <c>subject_type / subject_id</c> attached to either Opportunity or Event;
/// for now only the Opportunity slice is migrated, so this entity carries a
/// plain <see cref="OpportunityId"/>. When the Event slice lands, either
/// add a sibling Guid column (split-FK) or move to a shared abstraction.
/// </summary>
public sealed class SuccessManagementCriterion : BaseAuditableEntity<Guid>
{
    public Guid OpportunityId { get; private set; }
    public string Output { get; private set; } = string.Empty;
    public string SuccessCriteria { get; private set; } = string.Empty;
    public string? Comment { get; private set; }

    private SuccessManagementCriterion() { }

    public SuccessManagementCriterion(
        Guid id,
        Guid opportunityId,
        string output,
        string successCriteria,
        string? comment = null)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("Output is required.", nameof(output));
        }
        if (string.IsNullOrWhiteSpace(successCriteria))
        {
            throw new ArgumentException("Success criteria is required.", nameof(successCriteria));
        }

        Id = id;
        OpportunityId = opportunityId;
        Output = output.Trim();
        SuccessCriteria = successCriteria.Trim();
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }
}
