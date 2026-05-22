using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Verifies the establishment-side offer-reject endpoint accepts the
/// legacy bare-POST shape (no body) in addition to the canonical
/// <c>{reason_id, other_reason?}</c> body.
/// </summary>
public sealed class LegacyBareEstablishmentRejectTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;

    private Guid _applicantEstablishmentId;
    private Guid _senderEstablishmentId;
    private Guid _offerId;

    private static readonly TestUser ApplicantOwner = new(
        Sub: "legacy-rej-applicant-owner", Roles: new[] { "matloob_user" });
    private static readonly TestUser SenderOwner = new(
        Sub: "legacy-rej-sender-owner", Roles: new[] { "matloob_user" });

    public LegacyBareEstablishmentRejectTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, ApplicantOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, SenderOwner.Sub);

        _applicantEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, ApplicantOwner.Sub, "CR-LEGACY-REJ-APP");
        _senderEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, SenderOwner.Sub, "CR-LEGACY-REJ-SEND");

        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishmentId, name: "Reject target", forVacancy: false);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId,
            applicantEstablishmentId: _applicantEstablishmentId,
            appliedByUserId: ApplicantOwner.Sub);
        _offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishmentId, oppId, appId,
            sentByUserId: SenderOwner.Sub,
            status: OfferStatus.Pending);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LegacyBarePost_NoBody_Returns200_AndFlipsToRejected()
    {
        var client = _factory.CreateClientFor(ApplicantOwner);
        // Bare POST: no content.
        var response = await client.PostAsync(
            $"/api/establishments/offers/{_offerId}/reject?establishment_id={_applicantEstablishmentId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Rejected", doc.RootElement.GetProperty("status").GetString());

        // Confirm DB row has null reason on the persisted offer (the
        // bare-POST path doesn't supply one).
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var offer = await db.Offers.FirstAsync(o => o.Id == _offerId);
        Assert.Equal(OfferStatus.Rejected, offer.Status);
        Assert.Null(offer.OfferRejectionReasonId);
    }

    [Fact]
    public async Task LegacyBarePost_EmptyJsonBody_Returns200()
    {
        var client = _factory.CreateClientFor(ApplicantOwner);
        var response = await client.PostAsync(
            $"/api/establishments/offers/{_offerId}/reject?establishment_id={_applicantEstablishmentId}",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CanonicalBodyWithReason_StillWorks()
    {
        // Seed a different offer so we don't conflict with the no-body test
        // (an already-rejected offer can't be rejected again).
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishmentId, name: "Reject target body", forVacancy: false);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId,
            applicantEstablishmentId: _applicantEstablishmentId,
            appliedByUserId: ApplicantOwner.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishmentId, oppId, appId,
            sentByUserId: SenderOwner.Sub,
            status: OfferStatus.Pending);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reasonId = await db.OfferRejectionReasons.Select(r => r.Id).FirstAsync();

        var client = _factory.CreateClientFor(ApplicantOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_applicantEstablishmentId}/offers/{offerId}/reject",
            new { reason_id = reasonId, other_reason = "different plan" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
