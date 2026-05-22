using Matloob.Domain.Offers;

namespace Matloob.Api.Tests.Oao;

/// <summary>
/// Offer state-machine invariants. Each state transition that the API will
/// drive must be allowed only from the documented prior status; invalid
/// transitions throw InvalidOperationException so endpoints can map them
/// to 422 invalid_status_transition.
/// </summary>
public sealed class OfferDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
    private static readonly DateTimeOffset Later = Now.AddDays(30);

    [Fact]
    public void Create_NoSponsor_StartsPending()
    {
        var offer = MakeOffer();
        Assert.Equal(OfferStatus.Pending, offer.Status);
        Assert.Null(offer.SponsorEstablishmentId);
    }

    [Fact]
    public void Create_WithSponsor_StartsPendingSponsorApproval()
    {
        var sponsor = Guid.NewGuid();
        var offer = MakeOffer(sponsor: sponsor);

        Assert.Equal(OfferStatus.PendingSponsorApproval, offer.Status);
        Assert.Equal(sponsor, offer.SponsorEstablishmentId);
    }

    [Fact]
    public void Accept_FromPending_FlipsToAcceptedAndStampsAcceptedAt()
    {
        var offer = MakeOffer();
        offer.Accept(Now);

        Assert.Equal(OfferStatus.Accepted, offer.Status);
        Assert.Equal(Now, offer.AcceptedAt);
    }

    [Fact]
    public void Accept_FromNonPending_Throws()
    {
        var offer = MakeOffer();
        offer.Reject(rejectionReasonId: Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => offer.Accept(Now));
    }

    [Fact]
    public void Reject_FromPending_FlipsToRejected_AndStoresReason()
    {
        var offer = MakeOffer();
        var reasonId = Guid.NewGuid();
        offer.Reject(reasonId, otherReason: "not a fit");

        Assert.Equal(OfferStatus.Rejected, offer.Status);
        Assert.Equal(reasonId, offer.OfferRejectionReasonId);
        Assert.Equal("not a fit", offer.OtherRejectionReason);
    }

    [Fact]
    public void RequestCancellation_FromAccepted_NoSponsor_GoesCancellationRequested()
    {
        var offer = MakeOffer();
        offer.Accept(Now);
        offer.RequestCancellation();

        Assert.Equal(OfferStatus.CancellationRequested, offer.Status);
    }

    [Fact]
    public void RequestCancellation_FromAccepted_WithSponsor_GoesPendingSponsorCancellation()
    {
        var sponsor = Guid.NewGuid();
        var offer = MakeOffer(sponsor: sponsor);
        offer.SponsorApprove();
        offer.Accept(Now);
        offer.RequestCancellation();

        Assert.Equal(OfferStatus.PendingSponsorCancellationApproval, offer.Status);
    }

    [Fact]
    public void RequestCancellation_FromPending_Throws()
    {
        var offer = MakeOffer();
        Assert.Throws<InvalidOperationException>(() => offer.RequestCancellation());
    }

    [Fact]
    public void ApproveCancellation_FromCancellationRequested_GoesCanceled()
    {
        var offer = MakeOffer();
        offer.Accept(Now);
        offer.RequestCancellation();
        offer.ApproveCancellation();

        Assert.Equal(OfferStatus.Canceled, offer.Status);
    }

    [Fact]
    public void RejectCancellation_FromCancellationRequested_GoesBackToAccepted()
    {
        var offer = MakeOffer();
        offer.Accept(Now);
        offer.RequestCancellation();
        offer.RejectCancellation();

        Assert.Equal(OfferStatus.Accepted, offer.Status);
    }

    [Fact]
    public void SponsorApprove_FromPendingSponsorApproval_FlipsToPending()
    {
        var offer = MakeOffer(sponsor: Guid.NewGuid());
        offer.SponsorApprove();
        Assert.Equal(OfferStatus.Pending, offer.Status);
    }

    [Fact]
    public void SponsorReject_FromPendingSponsorApproval_FlipsToSponsorRejected()
    {
        var offer = MakeOffer(sponsor: Guid.NewGuid());
        var reasonId = Guid.NewGuid();
        offer.SponsorReject(reasonId, otherReason: "sponsor said no");

        Assert.Equal(OfferStatus.SponsorRejected, offer.Status);
        Assert.Equal(reasonId, offer.OfferRejectionReasonId);
    }

    [Fact]
    public void MarkExpired_FromPending_FlipsToExpired()
    {
        var offer = MakeOffer();
        offer.MarkExpired();
        Assert.Equal(OfferStatus.Expired, offer.Status);
    }

    [Fact]
    public void MarkExpired_FromAccepted_Throws()
    {
        var offer = MakeOffer();
        offer.Accept(Now);
        Assert.Throws<InvalidOperationException>(() => offer.MarkExpired());
    }

    [Fact]
    public void Create_InvalidValidityWindow_Throws()
    {
        Assert.Throws<ArgumentException>(() => MakeOffer(
            validityFrom: Now,
            validityTo: Now.AddMinutes(-1)));
    }

    [Fact]
    public void Create_EndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentException>(() => MakeOffer(
            startDate: DateOnly.FromDateTime(Now.UtcDateTime),
            endDate: DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1)));
    }

    [Fact]
    public void Create_NegativeMonthlySalary_Throws()
    {
        Assert.Throws<ArgumentException>(() => MakeOffer(monthlySalary: -1m));
    }

    // No Ajeer/Contract types are exposed by the Offer aggregate. This test
    // is a structural reminder — if a future change adds an Ajeer column it
    // will fail to compile against the asserted shape, drawing attention
    // back to docs/25-ajeer-disposition.md.
    [Fact]
    public void Offer_HasNoAjeerOrContractMembers()
    {
        var offerType = typeof(Offer);
        foreach (var prop in offerType.GetProperties())
        {
            var name = prop.Name.ToLowerInvariant();
            Assert.DoesNotContain("ajeer", name);
            Assert.DoesNotContain("contract", name);
            Assert.DoesNotContain("invoice", name);
        }
    }

    private static Offer MakeOffer(
        Guid? sponsor = null,
        DateTimeOffset? validityFrom = null,
        DateTimeOffset? validityTo = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        decimal monthlySalary = 5000m)
    {
        return Offer.Create(
            id: Guid.NewGuid(),
            senderEstablishmentId: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            applicationId: Guid.NewGuid(),
            sentByUserId: "sub-sender",
            offerValidityFrom: validityFrom ?? Now,
            offerValidityTo: validityTo ?? Later,
            startDate: startDate ?? DateOnly.FromDateTime(Later.UtcDateTime).AddDays(1),
            endDate: endDate ?? DateOnly.FromDateTime(Later.UtcDateTime).AddDays(10),
            monthlySalary: monthlySalary,
            sponsorEstablishmentId: sponsor);
    }
}
