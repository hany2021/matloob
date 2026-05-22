using Matloob.Domain.Applications;

namespace Matloob.Api.Tests.Oao;

/// <summary>
/// Domain-level invariants for OpportunityApplication. The DB CHECK
/// constraint enforcing "exactly one applicant target" is covered by
/// Postgres in production but EF InMemory cannot evaluate it; the factory
/// methods make the same invariant impossible to violate from C# code by
/// only exposing per-type constructors that hard-wire the other half of
/// the tuple to null.
/// </summary>
public sealed class OpportunityApplicationDomainTests
{
    [Fact]
    public void ForUser_SetsUserSlotOnly()
    {
        var app = OpportunityApplication.ForUser(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            applicantUserId: "sub-1");

        Assert.Equal("sub-1", app.ApplicantUserId);
        Assert.Null(app.ApplicantEstablishmentId);
        Assert.Null(app.AppliedByUserId);
    }

    [Fact]
    public void ForUser_BlankSub_Throws()
    {
        Assert.Throws<ArgumentException>(() => OpportunityApplication.ForUser(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            applicantUserId: ""));
    }

    [Fact]
    public void ForEstablishment_SetsEstablishmentSlotOnly_AndCarriesAppliedBy()
    {
        var establishmentId = Guid.NewGuid();
        var app = OpportunityApplication.ForEstablishment(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            applicantEstablishmentId: establishmentId,
            appliedByUserId: "sub-applier");

        Assert.Null(app.ApplicantUserId);
        Assert.Equal(establishmentId, app.ApplicantEstablishmentId);
        Assert.Equal("sub-applier", app.AppliedByUserId);
    }

    [Fact]
    public void ForEstablishment_BlankAppliedBy_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            OpportunityApplication.ForEstablishment(
                id: Guid.NewGuid(),
                opportunityId: Guid.NewGuid(),
                applicantEstablishmentId: Guid.NewGuid(),
                appliedByUserId: " "));
    }
}
