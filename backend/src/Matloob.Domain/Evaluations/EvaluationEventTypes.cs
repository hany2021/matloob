namespace Matloob.Domain.Evaluations;

/// <summary>
/// Canonical outbox <c>event_type</c> string constants for the
/// Evaluation aggregate. DO NOT rename — downstream subscribers key off
/// the literal value.
/// </summary>
public static class EvaluationEventTypes
{
    public const string Submitted = "evaluation.submitted";
}
