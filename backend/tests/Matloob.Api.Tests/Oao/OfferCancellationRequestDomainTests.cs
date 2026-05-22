using Matloob.Domain.Offers;

namespace Matloob.Api.Tests.Oao;

/// <summary>
/// OfferCancellationRequest domain guards. The DB-side partial-unique
/// "at most one open request per offer" index is verified by Postgres in
/// production; here we cover the entity-side invariants (cannot approve
/// or reject twice).
/// </summary>
public sealed class OfferCancellationRequestDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void ByUser_SetsUserSlotOnly()
    {
        var req = OfferCancellationRequest.ByUser(
            id: Guid.NewGuid(),
            offerId: Guid.NewGuid(),
            requestedByUserId: "sub-1",
            cancellationReasonId: Guid.NewGuid(),
            otherReason: null,
            requestedAt: Now);

        Assert.Equal("sub-1", req.RequestedByUserId);
        Assert.Null(req.RequestedByEstablishmentId);
        Assert.False(req.IsApproved);
        Assert.False(req.IsRejected);
    }

    [Fact]
    public void ByEstablishment_SetsEstablishmentSlotOnly()
    {
        var est = Guid.NewGuid();
        var req = OfferCancellationRequest.ByEstablishment(
            id: Guid.NewGuid(),
            offerId: Guid.NewGuid(),
            requestedByEstablishmentId: est,
            cancellationReasonId: Guid.NewGuid(),
            otherReason: "structural",
            requestedAt: Now);

        Assert.Null(req.RequestedByUserId);
        Assert.Equal(est, req.RequestedByEstablishmentId);
        Assert.Equal("structural", req.OtherReason);
    }

    [Fact]
    public void Approve_FlipsIsApproved_AndStampsReviewer()
    {
        var req = MakeOpen();
        req.Approve("sub-reviewer", Now);

        Assert.True(req.IsApproved);
        Assert.False(req.IsRejected);
        Assert.Equal(Now, req.ReviewedAt);
        Assert.Equal("sub-reviewer", req.ReviewedByUserId);
    }

    [Fact]
    public void Reject_FlipsIsRejected_AndStampsReviewer()
    {
        var req = MakeOpen();
        req.Reject("sub-reviewer", Now);

        Assert.True(req.IsRejected);
        Assert.False(req.IsApproved);
    }

    [Fact]
    public void Approve_TwiceThrows()
    {
        var req = MakeOpen();
        req.Approve("sub-1", Now);
        Assert.Throws<InvalidOperationException>(() => req.Approve("sub-1", Now));
    }

    [Fact]
    public void Reject_AfterApprove_Throws()
    {
        var req = MakeOpen();
        req.Approve("sub-1", Now);
        Assert.Throws<InvalidOperationException>(() => req.Reject("sub-2", Now));
    }

    [Fact]
    public void ByUser_BlankSub_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            OfferCancellationRequest.ByUser(
                id: Guid.NewGuid(),
                offerId: Guid.NewGuid(),
                requestedByUserId: "",
                cancellationReasonId: null,
                otherReason: null,
                requestedAt: Now));
    }

    private static OfferCancellationRequest MakeOpen() =>
        OfferCancellationRequest.ByUser(
            id: Guid.NewGuid(),
            offerId: Guid.NewGuid(),
            requestedByUserId: "sub-1",
            cancellationReasonId: null,
            otherReason: null,
            requestedAt: Now);
}
