using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.Show;

/// <summary>
/// Builds the Laravel <c>UserResource</c>-shaped <see cref="ProfileResponse"/>
/// for a given user by projecting the migrated profile tables + reference
/// lookups. Shared by <see cref="GetProfileEndpoint"/> and every profile
/// mutator endpoint (which all return the refreshed UserResource, wrapped in a
/// <c>{ data }</c> envelope, exactly like the old Laravel controllers).
/// </summary>
public static class ProfileReadMapper
{
    private static string AssetUrl(Guid assetId) => $"/api/v1/assets/{assetId}";

    public static async Task<ProfileResponse> BuildAsync(
        AppDbContext db,
        User user,
        CancellationToken ct)
    {
        var userId = user.Id;

        var education = await db.UserEducation.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.GraduationYear)
            .ToListAsync(ct);

        var experiences = await db.UserExperiences.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.From)
            .ToListAsync(ct);

        var certificates = await db.UserCertificates.AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        var skills = await db.UserSkills.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var supportiveDocs = await db.SupportiveDocuments.AsNoTracking()
            .Where(d => d.UserId == userId)
            .ToListAsync(ct);

        var languages = await (
            from ul in db.UserLanguages.AsNoTracking().Where(x => x.UserId == userId)
            join l in db.Languages.AsNoTracking() on ul.LanguageId equals l.Id
            select new { l.Id, l.Name, ul.Level }).ToListAsync(ct);

        var professions = await (
            from up in db.UserProfessions.AsNoTracking().Where(x => x.UserId == userId)
            join c in db.OpportunityCategories.AsNoTracking() on up.OpportunityCategoryId equals c.Id
            select new { c.Id, c.Title, c.Description, c.Icon, c.ForVacancy, c.IsOther, up.Other })
            .ToListAsync(ct);

        var bank = user.BankAccountId is null ? null : await (
            from ba in db.BankAccounts.AsNoTracking().Where(x => x.Id == user.BankAccountId)
            join b in db.Banks.AsNoTracking() on ba.BankId equals b.Id
            select new { ba.Id, ba.Name, ba.Iban, BankId = b.Id, BankName = b.Name })
            .FirstOrDefaultAsync(ct);

        var city = user.CityId is null ? null : await db.Cities.AsNoTracking()
            .Where(c => c.Id == user.CityId).Select(c => new RefDto(c.Id, c.Name)).FirstOrDefaultAsync(ct);
        var region = user.RegionId is null ? null : await db.Regions.AsNoTracking()
            .Where(r => r.Id == user.RegionId).Select(r => new RefDto(r.Id, r.Name)).FirstOrDefaultAsync(ct);
        var nationality = user.NationalityId is null ? null : await db.Nationalities.AsNoTracking()
            .Where(n => n.Id == user.NationalityId).Select(n => new RefDto(n.Id, n.Name)).FirstOrDefaultAsync(ct);

        // ---- Profile completion (mirrors Laravel UserSupport) ----
        var personalInfoDone = bank is not null
            && !string.IsNullOrEmpty(user.Bio)
            && !string.IsNullOrEmpty(user.Phone)
            && !string.IsNullOrEmpty(user.AdditionalPhone)
            && !string.IsNullOrEmpty(user.Email);
        var educationDone = education.Count > 0;
        var interestsDone = professions.Count > 0;
        var experiencesDone = user.YearsOfExperience > 0;

        var uncompleted = new List<string>();
        if (!personalInfoDone) uncompleted.Add("personal-info");
        if (!educationDone) uncompleted.Add("education-info");
        if (!interestsDone) uncompleted.Add("interests-info");
        if (!experiencesDone) uncompleted.Add("experiences-info");
        var percentage = (4 - uncompleted.Count) * 25;

        return new ProfileResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            PhoneNumber = user.Phone,
            IdentityId = user.IdentityId,

            IdNumber = user.IdNumber,
            Gender = user.Gender?.ToWire(),
            Nationality = nationality,
            Age = user.Age,
            DateOfBirth = user.DateOfBirth?.ToString("yyyy-MM-dd"),
            HijriDateOfBirth = null,
            Bio = user.Bio,
            AdditionalPhoneNumber = user.AdditionalPhone,
            YearsOfExperience = user.YearsOfExperience,
            PassportCopy = null,
            Photo = user.PhotoAssetId is null ? null : AssetUrl(user.PhotoAssetId.Value),

            Professions = professions
                .Select(p => (object)new ProfessionDto(
                    p.Id, p.Title, p.Description, p.Icon, p.ForVacancy, p.IsOther, p.Other))
                .ToList(),
            Experiences = experiences
                .Select(e => (object)new ExperienceDto(
                    e.Id, e.Company, e.Position,
                    e.From.ToString("yyyy-MM-dd"),
                    e.To?.ToString("yyyy-MM-dd"),
                    e.Current, e.Description,
                    e.Type.ToWire(), e.Type.ExperienceTypeLabel()))
                .ToList(),
            Certificates = certificates
                .Select(c => (object)new CertificateDto(
                    c.Id, c.Name, c.IssuedBy,
                    c.IssuedAt?.ToString("yyyy-MM-dd"),
                    c.CopyAssetId is null ? null : new AssetRefDto(c.CopyAssetId.Value, AssetUrl(c.CopyAssetId.Value))))
                .ToList(),
            Skills = skills
                .Select(s => (object)new SkillDto(s.Id, s.Name, s.Level.ToWire()))
                .ToList(),
            Education = education
                .Select(e => (object)new EducationDto(
                    e.Id, e.Degree.ToWire(), e.Degree.DegreeLabel(),
                    e.Specialization, e.GpaSystem, e.Gpa, e.GraduationYear,
                    e.CopyAssetId is null ? null : new AssetRefDto(e.CopyAssetId.Value, AssetUrl(e.CopyAssetId.Value))))
                .ToList(),
            City = city,
            Region = region,
            BankAccount = bank is null
                ? null
                : new BankAccountDto(bank.Id, bank.Name, new RefDto(bank.BankId, bank.BankName), bank.Iban),
            Languages = languages
                .Select(l => (object)new LanguageDto(l.Id, l.Name, l.Level.ToWire(), l.Level.LevelLabel()))
                .ToList(),
            SupportiveDocuments = supportiveDocs
                .Select(d => (object)new SupportiveDocumentDto(
                    d.Id, d.Name,
                    d.FileAssetId is null ? null : new AssetRefDto(d.FileAssetId.Value, AssetUrl(d.FileAssetId.Value)),
                    d.Url))
                .ToList(),
            Participations = [],
            ProfileCompletePercentage = percentage,
            Evaluations = [],
            Reviews = [],
            Rate = null,
            TotalReviews = null,
            Onboarded = user.Onboarded,
            UncompletedProfileSections = uncompleted,
        };
    }
}
