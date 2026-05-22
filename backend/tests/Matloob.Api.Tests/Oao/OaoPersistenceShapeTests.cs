using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Persistence.Interceptors;
using Matloob.Domain.Applications;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Tests.Oao;

/// <summary>
/// Persistence-shape tests. EF Core InMemory does NOT enforce CHECK
/// constraints or partial-unique indexes — those are verified by Postgres
/// in production and reflected in the migration scripts. The tests here
/// verify:
/// <list type="bullet">
/// <item>The DbSets are reachable from AppDbContext.</item>
/// <item>EF can build the model — i.e. every IEntityTypeConfiguration is
///   internally consistent (no missing FK target, no duplicate index
///   name).</item>
/// <item>No contracts / invoices / ajeer table is created.</item>
/// <item>The OAO entities round-trip through SaveChanges in InMemory
///   (smoke test for the configurations).</item>
/// </list>
///
/// DB-only constraint coverage that lives in the migration (NOT here):
/// <list type="bullet">
/// <item><c>ck_opportunity_applications_applicant_one_of</c> — CHECK.</item>
/// <item><c>ux_opportunity_applications_user_active</c> + sibling — partial unique.</item>
/// <item><c>ux_offer_cancellation_requests_open_per_offer</c> — partial unique.</item>
/// <item><c>ck_evaluations_evaluable_one_of</c> / <c>_evaluator_one_of</c> — CHECK.</item>
/// <item><c>ux_evaluations_offer_evaluator_user</c> / <c>_establishment</c> — partial unique.</item>
/// </list>
/// </summary>
public sealed class OaoPersistenceShapeTests : IDisposable
{
    private readonly AppDbContext _db;

    public OaoPersistenceShapeTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"oao-shape-{Guid.NewGuid():N}")
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Model_BuildsSuccessfully_WithAllOaoConfigurations()
    {
        // Accessing Model forces EF to materialize the model. Any
        // misconfigured IEntityTypeConfiguration would throw here.
        var model = _db.Model;
        Assert.NotNull(model);

        Assert.NotNull(model.FindEntityType(typeof(Opportunity)));
        Assert.NotNull(model.FindEntityType(typeof(OpportunityAsset)));
        Assert.NotNull(model.FindEntityType(typeof(SuccessManagementCriterion)));
        Assert.NotNull(model.FindEntityType(typeof(SuccessManagementCriterionAsset)));
        Assert.NotNull(model.FindEntityType(typeof(OpportunityApplication)));
        Assert.NotNull(model.FindEntityType(typeof(Offer)));
        Assert.NotNull(model.FindEntityType(typeof(OfferCancellationRequest)));
        Assert.NotNull(model.FindEntityType(typeof(Evaluation)));
        Assert.NotNull(model.FindEntityType(typeof(EvaluationAsset)));
    }

    [Fact]
    public void Model_DoesNotIncludeAjeerOrContractOrInvoiceEntities()
    {
        var model = _db.Model;
        foreach (var entityType in model.GetEntityTypes())
        {
            var name = entityType.GetTableName()?.ToLowerInvariant() ?? string.Empty;
            Assert.DoesNotContain("ajeer", name);
            Assert.DoesNotContain("contract", name);
            Assert.DoesNotContain("invoice", name);
        }
    }

    [Fact]
    public async Task Opportunity_PersistsAndRoundTrips()
    {
        var now = DateTimeOffset.UtcNow;
        var opp = Opportunity.Create(
            id: Guid.NewGuid(),
            issuerEstablishmentId: Guid.NewGuid(),
            eventId: Guid.NewGuid(),
            opportunityCategoryId: Guid.NewGuid(),
            name: "Servers",
            description: "Description text long enough.",
            startDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(2),
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(10),
            locationTitle: "Riyadh",
            latitude: 24.7m,
            longitude: 46.6m,
            requiredPersonnel: 10,
            now: now,
            establishmentClassifications: EstablishmentClassification.Small | EstablishmentClassification.Medium,
            genders: OpportunityGender.Male | OpportunityGender.Female);

        _db.Opportunities.Add(opp);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Opportunities.AsNoTracking()
            .FirstAsync(o => o.Id == opp.Id);
        Assert.Equal("Servers", reloaded.Name);
        Assert.Equal(
            EstablishmentClassification.Small | EstablishmentClassification.Medium,
            reloaded.EstablishmentClassifications);
        Assert.Equal(
            OpportunityGender.Male | OpportunityGender.Female,
            reloaded.Genders);
        Assert.Equal(OpportunityStatus.Upcoming, reloaded.Status);
    }

    [Fact]
    public async Task OpportunityApplication_PersistsAndRoundTrips()
    {
        var app = OpportunityApplication.ForUser(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            applicantUserId: "sub-worker");
        _db.OpportunityApplications.Add(app);
        await _db.SaveChangesAsync();

        var reloaded = await _db.OpportunityApplications.AsNoTracking()
            .FirstAsync(a => a.Id == app.Id);
        Assert.Equal("sub-worker", reloaded.ApplicantUserId);
        Assert.Null(reloaded.ApplicantEstablishmentId);
    }

    [Fact]
    public async Task Offer_PersistsAndRoundTrips()
    {
        var offer = Offer.Create(
            id: Guid.NewGuid(),
            senderEstablishmentId: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            applicationId: Guid.NewGuid(),
            sentByUserId: "sub-sender",
            offerValidityFrom: DateTimeOffset.UtcNow,
            offerValidityTo: DateTimeOffset.UtcNow.AddDays(30),
            startDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(31),
            endDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40),
            monthlySalary: 5000m);
        _db.Offers.Add(offer);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Offers.AsNoTracking()
            .FirstAsync(o => o.Id == offer.Id);
        Assert.Equal(OfferStatus.Pending, reloaded.Status);
        Assert.Equal(Currency.SAR, reloaded.Currency);
    }

    [Fact]
    public async Task Evaluation_PersistsAndRoundTrips_WithOfferIdNotContractId()
    {
        var offerId = Guid.NewGuid();
        var eval = Evaluation.ByUserOfEstablishment(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            offerId: offerId,
            evaluatorUserId: "sub-evaluator",
            evaluableEstablishmentId: Guid.NewGuid(),
            rating: 5,
            recommendForFutureOpportunities: true);
        _db.Evaluations.Add(eval);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Evaluations.AsNoTracking()
            .FirstAsync(e => e.Id == eval.Id);
        Assert.Equal(offerId, reloaded.OfferId);
        // Confirm no contract pointer leaked through reflection.
        var props = typeof(Evaluation).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.DoesNotContain(props, p => p.Contains("contract"));
    }
}
