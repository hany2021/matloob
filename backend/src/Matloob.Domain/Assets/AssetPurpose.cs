namespace Matloob.Domain.Assets;

/// <summary>
/// Why an asset was uploaded. Drives validation (allowed MIME types per
/// purpose) and reference-counting / cleanup policies.
///
/// Each new purpose category lives here; do NOT add ad-hoc strings to the
/// column — the DB stores the enum as text so a typo becomes a build error.
/// </summary>
public enum AssetPurpose
{
    /// <summary>Free-form upload not yet bound to a domain object.</summary>
    Generic = 0,

    /// <summary>
    /// Authorization letter for the EstablishmentOnboarding flow.
    /// PDF / JPEG / PNG, ≤ 10 MB (see docs/15-establishment-onboarding-spec.md §3.3).
    /// </summary>
    AuthorizationLetter = 1,

    /// <summary>
    /// Commercial registration document for the EstablishmentOnboarding flow.
    /// PDF / JPEG / PNG, ≤ 10 MB (see docs/15-establishment-onboarding-spec.md §3.3).
    /// </summary>
    CommercialRegistration = 2,

    /// <summary>
    /// Files migrated from the legacy Laravel <c>media</c> table that don't map
    /// to a current document slot. Read-only; no new uploads should land here.
    /// </summary>
    LegacyMedia = 3,
}
