namespace Matloob.Domain.Applications;

/// <summary>
/// Canonical outbox <c>event_type</c> string constants for the
/// OpportunityApplication aggregate. DO NOT rename — downstream
/// subscribers key off the string value.
/// </summary>
public static class ApplicationEventTypes
{
    public const string Submitted = "application.submitted";
    public const string Withdrawn = "application.withdrawn";
}
