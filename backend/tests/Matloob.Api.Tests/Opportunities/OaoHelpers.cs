using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Establishments;
using Matloob.Domain.Applications;
using Matloob.Domain.Assets;
using Matloob.Domain.Common;
using Matloob.Domain.Establishments;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Test fixtures + factories for the OAO read endpoints. OAO-2 has no
/// write endpoints, so opportunities + applications are seeded directly
/// against the DbContext.
/// </summary>
internal static class OaoHelpers
{
    public static readonly TestUser Worker = new(
        Sub: "oao-worker-1",
        Roles: new[] { "matloob_user" });

    public static readonly TestUser Worker2 = new(
        Sub: "oao-worker-2",
        Roles: new[] { "matloob_user" });

    public static readonly TestUser EstablishmentOwner = new(
        Sub: "oao-est-owner-1",
        Roles: new[] { "matloob_user" });

    public static readonly TestUser Outsider = new(
        Sub: "oao-outsider-1",
        Roles: new[] { "matloob_user" });

    /// <summary>
    /// Idempotent users-row seed (same shape as
    /// <c>Establishments.Helpers.SeedLocalUserAsync</c> but scoped to the
    /// OAO factory).
    /// </summary>
    public static async Task SeedLocalUserAsync(OpportunitiesApiFactory factory, string sub)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.IdentityId == sub);
        if (existing is not null) return;

        db.Users.Add(User.CreateFromIdentity(
            id: Guid.NewGuid(),
            identityId: sub,
            email: null, name: null, phone: null,
            firstSeenAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seed an Approved establishment with one EstablishmentMember
    /// (Owner) bound to <paramref name="ownerSub"/>. Returns the
    /// establishment id.
    /// </summary>
    public static async Task<Guid> SeedApprovedEstablishmentAsync(
        OpportunitiesApiFactory factory,
        string ownerSub,
        string crNumber)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Skip if an establishment with this CR number already exists.
        var existing = await db.Establishments
            .FirstOrDefaultAsync(e => e.CommercialRegistrationNumber == crNumber);
        if (existing is not null)
        {
            return existing.Id;
        }

        // Build the aggregate via reflection-friendly factory: the
        // Establishment public constructors require validation; the
        // simplest path here is to use the same domain methods the real
        // endpoints would use. Reuse the CreateDraft + Submit + Approve
        // flow would be heavy; for tests we set raw fields.
        var establishment = SeedEstablishmentDirect(db, ownerSub, crNumber);

        db.EstablishmentMembers.Add(new EstablishmentMember(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            userId: ownerSub,
            role: EstablishmentMemberRole.Owner,
            addedByUserId: ownerSub,
            addedAt: DateTimeOffset.UtcNow,
            isActive: true));

        await db.SaveChangesAsync();
        return establishment.Id;
    }

    /// <summary>
    /// Seed an Opportunity row directly. <paramref name="forVacancy"/>
    /// controls which category to use (uses an existing seeded category
    /// matching the flag).
    /// </summary>
    public static async Task<Guid> SeedOpportunityAsync(
        OpportunitiesApiFactory factory,
        Guid issuerEstablishmentId,
        bool forVacancy = true,
        string name = "Test Opportunity",
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        OpportunityStatus status = OpportunityStatus.Upcoming,
        int requiredPersonnel = 10,
        Guid? eventId = null)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = await db.OpportunityCategories
            .Where(c => c.ForVacancy == forVacancy && !c.IsOther && c.ParentId != null)
            .FirstAsync();

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var opp = Opportunity.Create(
            id: Guid.NewGuid(),
            issuerEstablishmentId: issuerEstablishmentId,
            eventId: eventId ?? Guid.NewGuid(),
            opportunityCategoryId: category.Id,
            name: name,
            description: "A long-enough description for the opportunity.",
            startDate: startDate ?? today.AddDays(7),
            endDate: endDate ?? today.AddDays(14),
            locationTitle: "Riyadh",
            latitude: 24.7m,
            longitude: 46.6m,
            requiredPersonnel: requiredPersonnel,
            now: now);

        // Override the auto-status if caller asked for something else
        // (e.g. Ended/Finished for negative tests). Status field has
        // private setters; reflect briefly through EF — but the simpler
        // path is to use EF.Property to set after Add. Cleaner: add a
        // small internal helper inside the entity. For now, just use
        // reflection.
        if (status != opp.Status)
        {
            typeof(Opportunity)
                .GetProperty(nameof(Opportunity.Status))!
                .SetValue(opp, status);
        }

        db.Opportunities.Add(opp);
        await db.SaveChangesAsync();
        return opp.Id;
    }

    /// <summary>
    /// Seed an application directly. Caller chooses user-side or
    /// establishment-side via the optional parameters.
    /// </summary>
    public static async Task<Guid> SeedApplicationAsync(
        OpportunitiesApiFactory factory,
        Guid opportunityId,
        string? applicantUserId = null,
        Guid? applicantEstablishmentId = null,
        string? appliedByUserId = null)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var app = applicantUserId is not null
            ? OpportunityApplication.ForUser(
                id: Guid.NewGuid(),
                opportunityId: opportunityId,
                applicantUserId: applicantUserId)
            : OpportunityApplication.ForEstablishment(
                id: Guid.NewGuid(),
                opportunityId: opportunityId,
                applicantEstablishmentId: applicantEstablishmentId!.Value,
                appliedByUserId: appliedByUserId ?? "oao-default-applier");

        db.OpportunityApplications.Add(app);
        await db.SaveChangesAsync();
        return app.Id;
    }

    /// <summary>
    /// Seed an Asset row owned by <paramref name="ownerSub"/> so a test can
    /// link it to an opportunity via the assets endpoint.
    /// </summary>
    public static async Task<Guid> SeedAssetAsync(
        OpportunitiesApiFactory factory,
        string ownerSub,
        string fileName = "doc.pdf",
        string contentType = "application/pdf")
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.Assets.Add(new Asset(
            id: id,
            originalFileName: fileName,
            storedFileName: $"{id:N}.pdf",
            contentType: contentType,
            sizeBytes: 1024,
            sha256: new string('a', 64),
            relativePath: $"test/{id:N}.pdf",
            storageDriver: AssetStorageDriver.Local,
            visibility: AssetVisibility.Private,
            purpose: AssetPurpose.Generic,
            ownerUserId: ownerSub));
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Fetch an opportunity directly to inspect properties (e.g. status,
    /// ended_at) after a mutation endpoint call.
    /// </summary>
    public static async Task<Opportunity?> LoadOpportunityAsync(
        OpportunitiesApiFactory factory,
        Guid id)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Opportunities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    /// <summary>
    /// Fetch an offer directly to inspect status / AcceptedAt after a
    /// lifecycle endpoint call.
    /// </summary>
    public static async Task<Matloob.Domain.Offers.Offer?> LoadOfferAsync(
        OpportunitiesApiFactory factory,
        Guid id)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Offers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    /// <summary>
    /// Seed an Offer row for a (sender, opportunity, application) triple.
    /// <paramref name="status"/> optionally overrides the factory-computed
    /// initial status so tests can stage Accepted/etc directly.
    /// </summary>
    public static async Task<Guid> SeedOfferAsync(
        OpportunitiesApiFactory factory,
        Guid senderEstablishmentId,
        Guid opportunityId,
        Guid applicationId,
        string sentByUserId,
        Matloob.Domain.Offers.OfferStatus? status = null,
        Guid? sponsorEstablishmentId = null,
        decimal monthlySalary = 5000m,
        DateTimeOffset? acceptedAt = null)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var offer = Matloob.Domain.Offers.Offer.Create(
            id: Guid.NewGuid(),
            senderEstablishmentId: senderEstablishmentId,
            opportunityId: opportunityId,
            applicationId: applicationId,
            sentByUserId: sentByUserId,
            offerValidityFrom: now,
            offerValidityTo: now.AddDays(30),
            startDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(31),
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(40),
            monthlySalary: monthlySalary,
            sponsorEstablishmentId: sponsorEstablishmentId);

        if (status is { } s)
        {
            typeof(Matloob.Domain.Offers.Offer)
                .GetProperty(nameof(Matloob.Domain.Offers.Offer.Status))!
                .SetValue(offer, s);
        }
        if (acceptedAt is { } at)
        {
            typeof(Matloob.Domain.Offers.Offer)
                .GetProperty(nameof(Matloob.Domain.Offers.Offer.AcceptedAt))!
                .SetValue(offer, at);
        }

        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        return offer.Id;
    }

    /// <summary>
    /// Count outbox events emitted with a given type since seeding. Used
    /// by tests asserting opportunity.created / .updated / .ended / .deleted.
    /// </summary>
    public static async Task<int> CountOutboxEventsAsync(
        OpportunitiesApiFactory factory,
        string eventType,
        Guid? aggregateId = null)
    {
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var q = db.OutboxEvents.AsNoTracking().Where(e => e.EventType == eventType);
        if (aggregateId is { } id)
        {
            q = q.Where(e => e.AggregateId == id);
        }
        return await q.CountAsync();
    }

    // -- internals -----------------------------------------------------------

    private static Establishment SeedEstablishmentDirect(
        AppDbContext db,
        string ownerSub,
        string crNumber)
    {
        // Use reflection sparingly: the Establishment aggregate root has
        // private setters and a complex lifecycle. For test fixtures we
        // poke the values directly. Production code goes through the
        // domain transitions.
        var establishment = (Establishment)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(Establishment));

        var type = typeof(Establishment);
        SetProp(type, establishment, "Id", Guid.NewGuid());
        SetProp(type, establishment, "Name", $"Test Establishment {crNumber}");
        SetProp(type, establishment, "CommercialRegistrationNumber", crNumber);
        SetProp(type, establishment, "LaborOfficeId", "12345");
        SetProp(type, establishment, "SequenceNumber", "67890");
        SetProp(type, establishment, "City", "Riyadh");
        SetProp(type, establishment, "Email", "owner@example.test");
        SetProp(type, establishment, "Phone", "+966500000000");
        SetProp(type, establishment, "Status", EstablishmentStatus.Approved);
        SetProp(type, establishment, "CreatedByUserId", ownerSub);

        // Base audit fields.
        var baseType = typeof(BaseAuditableEntity<Guid>);
        SetProp(baseType, establishment, "CreatedAt", DateTimeOffset.UtcNow);

        db.Establishments.Add(establishment);
        return establishment;
    }

    private static void SetProp(Type t, object instance, string name, object? value) =>
        t.GetProperty(name,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);
}
