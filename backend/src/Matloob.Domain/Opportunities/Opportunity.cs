using Matloob.Domain.Common;

namespace Matloob.Domain.Opportunities;

/// <summary>
/// Opportunity aggregate root. Mirrors the legacy Laravel
/// <c>opportunities</c> table with all Ajeer / contract / invoice columns
/// removed (none existed on this entity directly, but the related
/// <c>contracts_count</c> projection in OfferResource is dropped — see
/// docs/25-ajeer-disposition.md).
///
/// <para>
/// <b>Foreign keys.</b> <see cref="EventId"/> is stored as a bare Guid: the
/// Event entity does not exist yet in the new system (events slice is in a
/// later sprint). Same for <see cref="JobTitleCategoryId"/> — left for the
/// time being on the offer rather than here. <see cref="CityId"/> and
/// <see cref="NationalityId"/> FK the existing reference-lookup tables when
/// EF configuration runs; the relationships are declared in the
/// configuration class to keep the domain type free of EF-isms.
/// </para>
///
/// <para>
/// <b>Lifecycle.</b> New opportunities created via the API go straight to
/// <see cref="OpportunityStatus.Upcoming"/> or
/// <see cref="OpportunityStatus.Active"/> based on the start_date
/// (Q-OPP-1 default — see docs/40-api-migration-readiness.md). Drafted
/// remains in the enum only for legacy import.
/// </para>
/// </summary>
public sealed class Opportunity : BaseAuditableEntity<Guid>, IAggregateRoot
{
    // --- Required relationships ---------------------------------------------
    public Guid IssuerEstablishmentId { get; private set; }

    /// <summary>
    /// FK to the future Event entity (events slice has not landed). Stored
    /// as a bare Guid for now; the FK declaration is added when the Event
    /// entity exists. TODO: wire up the FK in OpportunityConfiguration.
    /// </summary>
    public Guid EventId { get; private set; }

    public Guid OpportunityCategoryId { get; private set; }

    // --- Optional relationships ---------------------------------------------
    public Guid? CityId { get; private set; }
    public Guid? NationalityId { get; private set; }

    // --- Required content ---------------------------------------------------
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string LocationTitle { get; private set; } = string.Empty;

    /// <summary>Latitude — Postgres precision <c>numeric(8,6)</c>.</summary>
    public decimal Latitude { get; private set; }

    /// <summary>Longitude — Postgres precision <c>numeric(9,6)</c>.</summary>
    public decimal Longitude { get; private set; }

    public int RequiredPersonnel { get; private set; }

    // --- Optional content ---------------------------------------------------
    public decimal? MonthlySalary { get; private set; }
    public byte? YearsOfExperienceRequired { get; private set; }
    public WorkingHoursType? WorkingHoursType { get; private set; }
    public TimeOnly? WorkingHoursFrom { get; private set; }
    public TimeOnly? WorkingHoursTo { get; private set; }
    public decimal? Fees { get; private set; }
    public string? PhoneContactInformation { get; private set; }
    public string? EmailContactInformation { get; private set; }

    /// <summary>Bitflags — see <see cref="EstablishmentClassification"/>.</summary>
    public EstablishmentClassification EstablishmentClassifications { get; private set; }

    /// <summary>Bitflags — see <see cref="OpportunityGender"/>.</summary>
    public OpportunityGender Genders { get; private set; }

    // --- Lifecycle ----------------------------------------------------------
    public OpportunityStatus Status { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? EndedByUserId { get; private set; }

    private Opportunity() { }

    /// <summary>
    /// Factory used by the create endpoint. Computes the initial status from
    /// <paramref name="now"/> vs <paramref name="startDate"/> (Q-OPP-1
    /// default: Upcoming if start_date is in the future, Active if it has
    /// already started).
    /// </summary>
    public static Opportunity Create(
        Guid id,
        Guid issuerEstablishmentId,
        Guid eventId,
        Guid opportunityCategoryId,
        string name,
        string description,
        DateOnly startDate,
        DateOnly endDate,
        string locationTitle,
        decimal latitude,
        decimal longitude,
        int requiredPersonnel,
        DateTimeOffset now,
        Guid? cityId = null,
        Guid? nationalityId = null,
        decimal? monthlySalary = null,
        byte? yearsOfExperienceRequired = null,
        WorkingHoursType? workingHoursType = null,
        TimeOnly? workingHoursFrom = null,
        TimeOnly? workingHoursTo = null,
        decimal? fees = null,
        string? phoneContactInformation = null,
        string? emailContactInformation = null,
        EstablishmentClassification establishmentClassifications = EstablishmentClassification.None,
        OpportunityGender genders = OpportunityGender.None)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Opportunity name is required.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Opportunity description is required.", nameof(description));
        }
        if (endDate < startDate)
        {
            throw new ArgumentException("end_date must be on or after start_date.", nameof(endDate));
        }
        if (requiredPersonnel < 1)
        {
            throw new ArgumentException("required_personnel must be at least 1.", nameof(requiredPersonnel));
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var initialStatus = startDate > today
            ? OpportunityStatus.Upcoming
            : OpportunityStatus.Active;

        return new Opportunity
        {
            Id = id,
            IssuerEstablishmentId = issuerEstablishmentId,
            EventId = eventId,
            OpportunityCategoryId = opportunityCategoryId,
            CityId = cityId,
            NationalityId = nationalityId,
            Name = name.Trim(),
            Description = description.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            LocationTitle = locationTitle.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            RequiredPersonnel = requiredPersonnel,
            MonthlySalary = monthlySalary,
            YearsOfExperienceRequired = yearsOfExperienceRequired,
            WorkingHoursType = workingHoursType,
            WorkingHoursFrom = workingHoursFrom,
            WorkingHoursTo = workingHoursTo,
            Fees = fees,
            PhoneContactInformation = phoneContactInformation,
            EmailContactInformation = emailContactInformation,
            EstablishmentClassifications = establishmentClassifications,
            Genders = genders,
            Status = initialStatus,
        };
    }

    /// <summary>
    /// Patch the editable subset. Caller supplies all fields it wants to
    /// keep; null overwrites only on nullable columns. Status is NOT
    /// editable via this method — use <see cref="End"/>.
    /// </summary>
    public void Update(
        string? name = null,
        string? description = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? locationTitle = null,
        decimal? latitude = null,
        decimal? longitude = null,
        int? requiredPersonnel = null,
        Guid? opportunityCategoryId = null,
        Guid? cityId = null,
        Guid? nationalityId = null,
        decimal? monthlySalary = null,
        byte? yearsOfExperienceRequired = null,
        WorkingHoursType? workingHoursType = null,
        TimeOnly? workingHoursFrom = null,
        TimeOnly? workingHoursTo = null,
        decimal? fees = null,
        string? phoneContactInformation = null,
        string? emailContactInformation = null,
        EstablishmentClassification? establishmentClassifications = null,
        OpportunityGender? genders = null)
    {
        if (name is not null) Name = name.Trim();
        if (description is not null) Description = description.Trim();
        if (startDate is not null) StartDate = startDate.Value;
        if (endDate is not null) EndDate = endDate.Value;
        if (locationTitle is not null) LocationTitle = locationTitle.Trim();
        if (latitude is not null) Latitude = latitude.Value;
        if (longitude is not null) Longitude = longitude.Value;
        if (requiredPersonnel is not null) RequiredPersonnel = requiredPersonnel.Value;
        if (opportunityCategoryId is not null) OpportunityCategoryId = opportunityCategoryId.Value;
        if (cityId is not null) CityId = cityId.Value;
        if (nationalityId is not null) NationalityId = nationalityId.Value;

        // Nullable-on-purpose fields: caller may explicitly null them by
        // passing null. The original is preserved only when the parameter
        // is omitted (using default). Since C# can't disambiguate between
        // "omitted" and "explicit null" for nullable parameters, we accept
        // the safer behaviour: omitted = no-op (cannot null these here).
        if (monthlySalary is not null) MonthlySalary = monthlySalary;
        if (yearsOfExperienceRequired is not null) YearsOfExperienceRequired = yearsOfExperienceRequired;
        if (workingHoursType is not null) WorkingHoursType = workingHoursType;
        if (workingHoursFrom is not null) WorkingHoursFrom = workingHoursFrom;
        if (workingHoursTo is not null) WorkingHoursTo = workingHoursTo;
        if (fees is not null) Fees = fees;
        if (phoneContactInformation is not null) PhoneContactInformation = phoneContactInformation;
        if (emailContactInformation is not null) EmailContactInformation = emailContactInformation;
        if (establishmentClassifications is not null) EstablishmentClassifications = establishmentClassifications.Value;
        if (genders is not null) Genders = genders.Value;

        if (EndDate < StartDate)
        {
            throw new InvalidOperationException("end_date must be on or after start_date.");
        }
    }

    /// <summary>
    /// Manually end an opportunity (the "End" button in the legacy UI).
    /// Allowed from <see cref="OpportunityStatus.Upcoming"/> or
    /// <see cref="OpportunityStatus.Active"/>.
    /// </summary>
    public void End(string endedByUserId, DateTimeOffset at)
    {
        if (Status != OpportunityStatus.Upcoming && Status != OpportunityStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot end opportunity in status {Status}.");
        }
        Status = OpportunityStatus.Ended;
        EndedAt = at;
        EndedByUserId = endedByUserId;
    }
}
