using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// One education entry for a <see cref="User"/>. Standalone child entity keyed
/// by <see cref="UserId"/> (mirrors the EstablishmentDocument pattern — not an
/// EF navigation collection on the aggregate). Maps the legacy
/// <c>user_education</c> table.
/// </summary>
public sealed class UserEducation : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public EducationDegree Degree { get; private set; }
    public string? Specialization { get; private set; }

    /// <summary>GPA scale: 4, 5, or 100 (validated at the endpoint).</summary>
    public int GpaSystem { get; private set; }
    public decimal Gpa { get; private set; }
    public int GraduationYear { get; private set; }

    /// <summary>Optional certificate scan stored via the Asset GUID flow.</summary>
    public Guid? CopyAssetId { get; private set; }

    private UserEducation() { }

    public UserEducation(
        Guid id,
        Guid userId,
        EducationDegree degree,
        string? specialization,
        int gpaSystem,
        decimal gpa,
        int graduationYear,
        Guid? copyAssetId = null)
    {
        Id = id;
        UserId = userId;
        Degree = degree;
        Specialization = specialization;
        GpaSystem = gpaSystem;
        Gpa = gpa;
        GraduationYear = graduationYear;
        CopyAssetId = copyAssetId;
    }

    public void Update(
        EducationDegree degree,
        string? specialization,
        int gpaSystem,
        decimal gpa,
        int graduationYear)
    {
        Degree = degree;
        Specialization = specialization;
        GpaSystem = gpaSystem;
        Gpa = gpa;
        GraduationYear = graduationYear;
    }

    public void SetCopy(Guid? assetId) => CopyAssetId = assetId;
}
