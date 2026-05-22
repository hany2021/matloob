namespace Matloob.Domain.Offers;

/// <summary>
/// Currency of an offer's monetary amounts. The Laravel system supported
/// only SAR; we preserve the same single-value enum for forward
/// compatibility (a future multi-currency rollout adds members without a
/// schema change).
/// </summary>
public enum Currency
{
    SAR = 0,
}
