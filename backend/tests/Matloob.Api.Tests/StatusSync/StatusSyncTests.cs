using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.StatusSync;
using Matloob.Api.Tests.Assets.Cleanup;
using Matloob.Domain.Events;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Matloob.Api.Tests.StatusSync;

/// <summary>
/// Unit tests for <see cref="StatusSyncService"/> — the time-driven status
/// reconciliation (legacy SyncEventsStatuses / SyncOffersStatuses). Each test
/// gets its own isolated in-memory database and a pinned clock, seeds domain
/// entities via their real factories/transitions, runs one sweep, and asserts
/// the transitions + idempotency.
/// </summary>
public sealed class StatusSyncTests
{
    private static readonly DateTimeOffset SyncNow = new(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 20);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"statussync-{Guid.NewGuid():N}")
            .Options);

    private static StatusSyncService Service(AppDbContext db, IOutboxWriter outbox) =>
        new(db, new TestTimeProvider(SyncNow), NullLogger<StatusSyncService>.Instance, outbox);

    private static StatusSyncService Service(AppDbContext db) =>
        Service(db, new RecordingOutboxWriter());

    /// <summary>In-test <see cref="IOutboxWriter"/> that records enqueued
    /// (eventType, aggregateId) pairs so emission can be asserted without the
    /// dispatcher.</summary>
    private sealed class RecordingOutboxWriter : IOutboxWriter
    {
        public List<(string EventType, Guid AggregateId)> Events { get; } = new();
        public void Enqueue(string eventType, string aggregateType, Guid aggregateId, object payload)
            => Events.Add((eventType, aggregateId));
        public void Flush() { }
    }

    // -- events ---------------------------------------------------------------

    [Fact]
    public async Task Events_StartArrived_Upcoming_BecomesActive()
    {
        using var db = NewDb();
        var e = BuildEvent(start: new(2026, 6, 10), end: new(2026, 6, 30), publishOn: new(2026, 6, 1));
        Assert.Equal(EventStatus.Upcoming, e.Status); // start was future at publish
        db.Events.Add(e);
        await db.SaveChangesAsync();

        var summary = await Service(db).RunAsync(default);

        Assert.Equal(1, summary.EventsStarted);
        Assert.Equal(EventStatus.Active, (await db.Events.SingleAsync()).Status);
    }

    [Fact]
    public async Task Events_EndPassed_Active_BecomesFinished_AndFutureStaysActive()
    {
        using var db = NewDb();
        var past = BuildEvent(start: new(2026, 6, 1), end: new(2026, 6, 10), publishOn: new(2026, 6, 1));
        var future = BuildEvent(start: new(2026, 6, 1), end: new(2026, 6, 30), publishOn: new(2026, 6, 1));
        Assert.Equal(EventStatus.Active, past.Status);
        Assert.Equal(EventStatus.Active, future.Status);
        db.Events.AddRange(past, future);
        await db.SaveChangesAsync();

        var summary = await Service(db).RunAsync(default);

        Assert.Equal(1, summary.EventsFinished);
        Assert.Equal(EventStatus.Finished, (await db.Events.SingleAsync(x => x.Id == past.Id)).Status);
        Assert.Equal(EventStatus.Active, (await db.Events.SingleAsync(x => x.Id == future.Id)).Status);
    }

    // -- opportunities --------------------------------------------------------

    [Fact]
    public async Task Opportunities_StartAndFinish_Transition()
    {
        using var db = NewDb();
        // Upcoming whose start arrived -> Active.
        var starting = Opportunity.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Starting", "d",
            startDate: new(2026, 6, 10), endDate: new(2026, 6, 30), "loc", 24.7m, 46.6m, 1,
            now: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(OpportunityStatus.Upcoming, starting.Status);
        // Active whose end passed -> Finished.
        var finishing = Opportunity.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Finishing", "d",
            startDate: new(2026, 6, 1), endDate: new(2026, 6, 10), "loc", 24.7m, 46.6m, 1,
            now: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(OpportunityStatus.Active, finishing.Status);
        db.Opportunities.AddRange(starting, finishing);
        await db.SaveChangesAsync();

        var summary = await Service(db).RunAsync(default);

        Assert.Equal(1, summary.OpportunitiesStarted);
        Assert.Equal(1, summary.OpportunitiesFinished);
        Assert.Equal(OpportunityStatus.Active, (await db.Opportunities.SingleAsync(x => x.Id == starting.Id)).Status);
        Assert.Equal(OpportunityStatus.Finished, (await db.Opportunities.SingleAsync(x => x.Id == finishing.Id)).Status);
    }

    // -- offers ---------------------------------------------------------------

    [Fact]
    public async Task Offers_ValidityExpired_Pending_BecomesExpired()
    {
        using var db = NewDb();
        var offer = Offer.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "sender-sub",
            offerValidityFrom: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            offerValidityTo: new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
            startDate: new(2026, 6, 25), endDate: new(2026, 7, 5), monthlySalary: 5000m);
        Assert.Equal(OfferStatus.Pending, offer.Status);
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var summary = await Service(db).RunAsync(default);

        Assert.Equal(1, summary.OffersExpired);
        Assert.Equal(OfferStatus.Expired, (await db.Offers.SingleAsync()).Status);
    }

    [Fact]
    public async Task Offers_JobEnded_Accepted_BecomesWaitingForEvaluation()
    {
        using var db = NewDb();
        var offer = Offer.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "sender-sub",
            offerValidityFrom: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            offerValidityTo: new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
            startDate: new(2026, 6, 1), endDate: new(2026, 6, 10), monthlySalary: 5000m);
        offer.Accept(new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(OfferStatus.Accepted, offer.Status);
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var summary = await Service(db).RunAsync(default);

        Assert.Equal(1, summary.OffersWaitingForEvaluation);
        Assert.Equal(0, summary.OffersExpired); // an Accepted offer is never expired
        Assert.Equal(OfferStatus.WaitingForEvaluation, (await db.Offers.SingleAsync()).Status);
    }

    // -- idempotency ----------------------------------------------------------

    [Fact]
    public async Task SecondRun_IsNoOp()
    {
        using var db = NewDb();
        db.Events.Add(BuildEvent(new(2026, 6, 1), new(2026, 6, 10), new(2026, 6, 1)));     // -> Finished
        var opp = Opportunity.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "n", "d",
            new(2026, 6, 1), new(2026, 6, 10), "loc", 24.7m, 46.6m, 1,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        db.Opportunities.Add(opp);
        await db.SaveChangesAsync();

        var first = await Service(db).RunAsync(default);
        Assert.True(first.TotalChanged >= 2);

        var second = await Service(db).RunAsync(default);
        Assert.Equal(0, second.TotalChanged);
    }

    // -- FYI outbox emission --------------------------------------------------

    [Fact]
    public async Task Events_Transitions_EmitFyiOutboxEvents()
    {
        using var db = NewDb();
        var starting = BuildEvent(start: new(2026, 6, 10), end: new(2026, 6, 30), publishOn: new(2026, 6, 1)); // Upcoming->Active
        var finishing = BuildEvent(start: new(2026, 6, 1), end: new(2026, 6, 10), publishOn: new(2026, 6, 1)); // Active->Finished
        db.Events.AddRange(starting, finishing);
        await db.SaveChangesAsync();

        var writer = new RecordingOutboxWriter();
        await Service(db, writer).RunAsync(default);

        Assert.Contains((EventEventTypes.Started, starting.Id), writer.Events);
        Assert.Contains((EventEventTypes.Finished, finishing.Id), writer.Events);
    }

    [Fact]
    public async Task Opportunities_Finish_EmitsExpiredEvent_StartDoesNot()
    {
        using var db = NewDb();
        var starting = Opportunity.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Starting", "d",
            startDate: new(2026, 6, 10), endDate: new(2026, 6, 30), "loc", 24.7m, 46.6m, 1,
            now: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var finishing = Opportunity.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Finishing", "d",
            startDate: new(2026, 6, 1), endDate: new(2026, 6, 10), "loc", 24.7m, 46.6m, 1,
            now: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        db.Opportunities.AddRange(starting, finishing);
        await db.SaveChangesAsync();

        var writer = new RecordingOutboxWriter();
        await Service(db, writer).RunAsync(default);

        // Finish → expired; start → no notification (legacy had none).
        Assert.Contains((OpportunityEventTypes.Expired, finishing.Id), writer.Events);
        Assert.DoesNotContain((OpportunityEventTypes.Expired, starting.Id), writer.Events);
    }

    // -- helpers --------------------------------------------------------------

    private static Event BuildEvent(DateOnly start, DateOnly end, DateOnly publishOn)
    {
        var e = new Event(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Event", "Desc", seasonId: null);
        e.ApplySchedule(start, end, "Riyadh", 24.7m, 46.6m, 100, 500, cityId: null);
        e.Publish(publishOn);
        return e;
    }
}
