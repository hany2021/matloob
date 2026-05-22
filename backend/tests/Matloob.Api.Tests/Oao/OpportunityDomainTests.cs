using Matloob.Domain.Opportunities;

namespace Matloob.Api.Tests.Oao;

/// <summary>
/// Domain-level invariants for the Opportunity aggregate. Status transitions,
/// validation, and the auto-status-from-start-date rule (Q-OPP-1 default).
/// </summary>
public sealed class OpportunityDomainTests
{
    [Fact]
    public void Create_StartDateInFuture_StartsUpcoming()
    {
        var now = DateTimeOffset.UtcNow;
        var future = DateOnly.FromDateTime(now.UtcDateTime).AddDays(7);
        var end = future.AddDays(3);

        var opp = MakeOpp(start: future, end: end, now: now);

        Assert.Equal(OpportunityStatus.Upcoming, opp.Status);
    }

    [Fact]
    public void Create_StartDateInPast_StartsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var past = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        var end = DateOnly.FromDateTime(now.UtcDateTime).AddDays(5);

        var opp = MakeOpp(start: past, end: end, now: now);

        Assert.Equal(OpportunityStatus.Active, opp.Status);
    }

    [Fact]
    public void Create_EndBeforeStart_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        Assert.Throws<ArgumentException>(() =>
            MakeOpp(start: today.AddDays(5), end: today.AddDays(1), now: now));
    }

    [Fact]
    public void Create_RequiredPersonnelLessThanOne_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        Assert.Throws<ArgumentException>(() => Opportunity.Create(
            id: Guid.NewGuid(),
            issuerEstablishmentId: Guid.NewGuid(),
            eventId: Guid.NewGuid(),
            opportunityCategoryId: Guid.NewGuid(),
            name: "Servers",
            description: "Description",
            startDate: today.AddDays(1),
            endDate: today.AddDays(2),
            locationTitle: "Riyadh",
            latitude: 24.7m,
            longitude: 46.6m,
            requiredPersonnel: 0,
            now: now));
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        Assert.Throws<ArgumentException>(() => Opportunity.Create(
            id: Guid.NewGuid(),
            issuerEstablishmentId: Guid.NewGuid(),
            eventId: Guid.NewGuid(),
            opportunityCategoryId: Guid.NewGuid(),
            name: "   ",
            description: "Description",
            startDate: today.AddDays(1),
            endDate: today.AddDays(2),
            locationTitle: "Riyadh",
            latitude: 24.7m,
            longitude: 46.6m,
            requiredPersonnel: 5,
            now: now));
    }

    [Fact]
    public void End_FromUpcoming_FlipsToEnded()
    {
        var now = DateTimeOffset.UtcNow;
        var opp = MakeOpp(
            start: DateOnly.FromDateTime(now.UtcDateTime).AddDays(5),
            end: DateOnly.FromDateTime(now.UtcDateTime).AddDays(10),
            now: now);

        opp.End("sub-1", now);

        Assert.Equal(OpportunityStatus.Ended, opp.Status);
        Assert.Equal(now, opp.EndedAt);
        Assert.Equal("sub-1", opp.EndedByUserId);
    }

    [Fact]
    public void End_FromEnded_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var opp = MakeOpp(
            start: DateOnly.FromDateTime(now.UtcDateTime).AddDays(5),
            end: DateOnly.FromDateTime(now.UtcDateTime).AddDays(10),
            now: now);
        opp.End("sub-1", now);

        Assert.Throws<InvalidOperationException>(() => opp.End("sub-2", now));
    }

    private static Opportunity MakeOpp(DateOnly start, DateOnly end, DateTimeOffset now) =>
        Opportunity.Create(
            id: Guid.NewGuid(),
            issuerEstablishmentId: Guid.NewGuid(),
            eventId: Guid.NewGuid(),
            opportunityCategoryId: Guid.NewGuid(),
            name: "Servers",
            description: "Long-enough description text.",
            startDate: start,
            endDate: end,
            locationTitle: "Riyadh",
            latitude: 24.7m,
            longitude: 46.6m,
            requiredPersonnel: 10,
            now: now);
}
