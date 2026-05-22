using Matloob.Domain.Evaluations;

namespace Matloob.Api.Tests.Oao;

/// <summary>
/// Evaluation domain invariants. The DB-side CHECK constraints
/// ("exactly one evaluable target" and "exactly one evaluator target")
/// are enforced by Postgres; the factory methods make it impossible to
/// construct a violating row from C# by exposing only typed builders.
///
/// Per Q-EVAL-1 the aggregate carries OfferId — NOT contract_id.
/// </summary>
public sealed class EvaluationDomainTests
{
    [Fact]
    public void ByUserOfEstablishment_SetsCorrectSlots()
    {
        var establishmentId = Guid.NewGuid();
        var eval = Evaluation.ByUserOfEstablishment(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            offerId: Guid.NewGuid(),
            evaluatorUserId: "sub-evaluator",
            evaluableEstablishmentId: establishmentId,
            rating: 5,
            recommendForFutureOpportunities: true,
            comment: "great");

        Assert.Equal("sub-evaluator", eval.EvaluatorUserId);
        Assert.Null(eval.EvaluatorEstablishmentId);
        Assert.Null(eval.EvaluableUserId);
        Assert.Equal(establishmentId, eval.EvaluableEstablishmentId);
        Assert.Equal((byte)5, eval.Rating);
    }

    [Fact]
    public void ByEstablishmentOfUser_SetsCorrectSlots()
    {
        var eval = Evaluation.ByEstablishmentOfUser(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            offerId: Guid.NewGuid(),
            evaluatorEstablishmentId: Guid.NewGuid(),
            evaluableUserId: "sub-worker",
            rating: 4,
            recommendForFutureOpportunities: false,
            matchingPercentage: 80);

        Assert.Null(eval.EvaluatorUserId);
        Assert.NotNull(eval.EvaluatorEstablishmentId);
        Assert.Equal("sub-worker", eval.EvaluableUserId);
        Assert.Null(eval.EvaluableEstablishmentId);
        Assert.Equal(80, eval.MatchingPercentage);
    }

    [Fact]
    public void ByEstablishmentOfEstablishment_SetsCorrectSlots()
    {
        var eval = Evaluation.ByEstablishmentOfEstablishment(
            id: Guid.NewGuid(),
            opportunityId: Guid.NewGuid(),
            offerId: Guid.NewGuid(),
            evaluatorEstablishmentId: Guid.NewGuid(),
            evaluableEstablishmentId: Guid.NewGuid(),
            rating: 3,
            recommendForFutureOpportunities: true);

        Assert.Null(eval.EvaluatorUserId);
        Assert.NotNull(eval.EvaluatorEstablishmentId);
        Assert.Null(eval.EvaluableUserId);
        Assert.NotNull(eval.EvaluableEstablishmentId);
    }

    [Fact]
    public void Create_RatingOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Evaluation.ByUserOfEstablishment(
                id: Guid.NewGuid(),
                opportunityId: Guid.NewGuid(),
                offerId: Guid.NewGuid(),
                evaluatorUserId: "sub",
                evaluableEstablishmentId: Guid.NewGuid(),
                rating: 6,
                recommendForFutureOpportunities: true));
    }

    [Fact]
    public void Create_MatchingPercentageOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Evaluation.ByEstablishmentOfUser(
                id: Guid.NewGuid(),
                opportunityId: Guid.NewGuid(),
                offerId: Guid.NewGuid(),
                evaluatorEstablishmentId: Guid.NewGuid(),
                evaluableUserId: "sub",
                rating: 3,
                recommendForFutureOpportunities: false,
                matchingPercentage: 101));
    }

    [Fact]
    public void Create_BlankEvaluatorUser_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Evaluation.ByUserOfEstablishment(
                id: Guid.NewGuid(),
                opportunityId: Guid.NewGuid(),
                offerId: Guid.NewGuid(),
                evaluatorUserId: " ",
                evaluableEstablishmentId: Guid.NewGuid(),
                rating: 5,
                recommendForFutureOpportunities: true));
    }

    // Structural reminder: Evaluation must NOT carry a contract reference.
    // If a future refactor reintroduces contract_id this test fails.
    [Fact]
    public void Evaluation_HasNoContractReference()
    {
        var evalType = typeof(Evaluation);
        foreach (var prop in evalType.GetProperties())
        {
            var name = prop.Name.ToLowerInvariant();
            Assert.DoesNotContain("contract", name);
        }
    }
}
