using Matloob.Domain.Common;

namespace Matloob.Domain.Opportunities;

/// <summary>
/// "Success criteria" entry the establishment sets when publishing an
/// opportunity OR an event. Legacy Laravel stored this as a polymorphic
/// <c>subject_type / subject_id</c>; here it is a split-FK — exactly one of
/// <see cref="OpportunityId"/> / <see cref="EventId"/> is set. Use the
/// <see cref="ForOpportunity"/> / <see cref="ForEvent"/> factories.
/// </summary>
public sealed class SuccessManagementCriterion : BaseAuditableEntity<Guid>
{
    public Guid? OpportunityId { get; private set; }
    public Guid? EventId { get; private set; }
    public string Output { get; private set; } = string.Empty;
    public string SuccessCriteria { get; private set; } = string.Empty;
    public string? Comment { get; private set; }

    private SuccessManagementCriterion() { }

    private SuccessManagementCriterion(Guid id, string output, string successCriteria, string? comment)
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
        Output = output.Trim();
        SuccessCriteria = successCriteria.Trim();
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public static SuccessManagementCriterion ForOpportunity(
        Guid id, Guid opportunityId, string output, string successCriteria, string? comment = null)
        => new(id, output, successCriteria, comment) { OpportunityId = opportunityId };

    public static SuccessManagementCriterion ForEvent(
        Guid id, Guid eventId, string output, string successCriteria, string? comment = null)
        => new(id, output, successCriteria, comment) { EventId = eventId };
}
