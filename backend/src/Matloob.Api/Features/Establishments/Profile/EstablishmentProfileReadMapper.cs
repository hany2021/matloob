using Matloob.Api.Features.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Profile;

/// <summary>
/// Builds the composite <see cref="EstablishmentMeProfileResponse"/> (Laravel
/// <c>EstablishmentResource</c> + <c>ProfileResource</c> shape) for one
/// establishment. Extracted from <see cref="GetMeProfileEndpoint"/> so the
/// profile mutator endpoints (general-info / contact-info / experience /
/// bank-account / logo) can all return the full refreshed resource — exactly
/// what Laravel's controllers did and what the frontend re-reads after a save.
/// </summary>
public static class EstablishmentProfileReadMapper
{
    /// <summary>Polymorphic <c>media</c> model type for establishment attachments.</summary>
    public const string ModelType = "Establishment";

    /// <summary>Media collection holding the single establishment logo.</summary>
    public const string LogoCollection = "logo";

    public static async Task<EstablishmentMeProfileResponse> BuildAsync(
        AppDbContext db, Establishment e, CancellationToken ct)
    {
        var services = await db.Services
            .AsNoTracking()
            .Where(s => s.EstablishmentId == e.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (object)new Services.ServiceResponse(s.Id, s.Name, s.Description))
            .ToListAsync(ct);

        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.EstablishmentId == e.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (object)new Products.ProductResponse(p.Id, p.Name, p.Description))
            .ToListAsync(ct);

        EstablishmentBankAccountBlock? bankAccount = e.BankAccountId is null ? null : await (
            from ba in db.BankAccounts.AsNoTracking().Where(x => x.Id == e.BankAccountId)
            join b in db.Banks.AsNoTracking() on ba.BankId equals b.Id
            select new EstablishmentBankAccountBlock(
                ba.Id, ba.Name, ba.Iban, new EstablishmentBankRef(b.Id, b.Name)))
            .FirstOrDefaultAsync(ct);

        var logo = await MediaSupport.ListAsync(db, ModelType, e.Id.ToString(), LogoCollection, ct);
        var logoUrl = logo.Count > 0 ? logo[0].Url : null;

        return BuildResponse(e, services, products, bankAccount, logoUrl);
    }

    private static EstablishmentMeProfileResponse BuildResponse(
        Establishment e,
        IReadOnlyList<object> services,
        IReadOnlyList<object> products,
        EstablishmentBankAccountBlock? bankAccount,
        string? logoUrl)
    {
        var general = new EstablishmentGeneralInfoBlock
        {
            Id = e.Id,
            EstablishmentStatus = e.Status.ToString(),
            EconomicActivity = e.EconomicActivity,
            SubEconomicActivity = e.SubEconomicActivity,
            CrNumber = e.CommercialRegistrationNumber,
            EstablishmentSize = e.EstablishmentSize,
            CrNumberExpiry = e.CommercialRegistrationExpiry?.ToString("yyyy-MM-dd"),
            YearsOfExperience = e.YearsOfExperience,
            Area = e.Area,
            Description = e.Description,
            Lat = e.Latitude,
            Lon = e.Longitude,
            LocationTitle = e.LocationTitle,
            City = e.City,
            Neighborhood = e.District,
            StreetName = e.Street,
            BuildingNumber = e.BuildingNumber,
            PostalCode = e.PostalCode,
            AdditionalNumber = e.AdditionalNumber,
            Website = e.Website,
        };

        var contact = new EstablishmentContactInfoBlock
        {
            // The old contact_info had its own UUID; we collapsed the
            // three legacy tables onto Establishment, so the id is the
            // same establishment id.
            Id = e.Id,
            ContactNumber = e.Phone,
            AdditionalContactNumber = e.AdditionalContactNumber,
            Email = e.Email,
        };

        var profile = new EstablishmentProfileBlock
        {
            GeneralInfo = general,
            ContactInfo = contact,
            Services = services,
            Products = products,
            BankAccount = bankAccount,
            // Placeholders -- backing concept not migrated yet.
            Participations = [],
            Evaluations = [],
            Reviews = [],
            Experiences = [],
        };

        return new EstablishmentMeProfileResponse
        {
            Id = e.Id,
            Name = e.Name,
            Email = e.Email,
            ProfileCompletePercentage = 0,
            Logo = logoUrl,
            Profile = profile,
            Rate = null,
            TotalReviews = null,
            CanManageEvents = e.CanManageEvents,
        };
    }
}
