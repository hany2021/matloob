using System.Net;
using System.Net.Http.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Establishments;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// API-driven QA golden path for establishment onboarding + event creation,
/// mapped to matloob-business-qa-v2.md §6.2 / §6.4. Dependency order:
/// register → submit → admin approve/reject → (approved organizer) create event.
/// Drives the real registration + admin-review endpoints.
/// </summary>
public sealed class QaOnboardingEventsGoldenPathTests
    : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;
    private static readonly Guid EventTypeId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    public QaOnboardingEventsGoldenPathTests(EstablishmentsApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await Helpers.SeedLocalUserAsync(_factory, Helpers.Creator.Sub);
        await Helpers.SeedLocalUserAsync(_factory, Helpers.OtherUser.Sub);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.EventTypes.AnyAsync(t => t.Id == EventTypeId))
        {
            db.EventTypes.Add(new EventType(EventTypeId, "Conference", "desc", "bg.png", "icon.png"));
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ===== §6.4 — Establishment onboarding ==================================

    /// <summary>TC-E10 / BR-11: an individual registers an establishment (with
    /// Authorization + CR documents) and submits → status PendingReview.</summary>
    [Fact]
    public async Task TC_E10_IndividualRegistersEstablishment_PendingReview()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await Helpers.CreateReadyToSubmitDraftAsync(_factory, creator, "CR-QA-E10");

        var submit = await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        Assert.Equal(EstablishmentStatus.PendingReview, await StatusOfAsync(id));
    }

    /// <summary>TC-E11 / BR-12: submitting without the Authorization + CR documents
    /// is blocked (the documents are mandatory).</summary>
    [Fact]
    public async Task TC_E11_SubmitWithoutDocuments_IsBlocked()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/basic-info",
            new
            {
                name = "Acme", commercialRegistrationNumber = "CR-QA-E11",
                laborOfficeId = "1", sequenceNumber = "1", city = "Riyadh",
                email = "ops@acme.test", phone = "+966500000000",
            });

        var submit = await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
    }

    /// <summary>TC-E12 / TC-M01 / BR-11: the admin approves a PendingReview
    /// establishment → status Approved (and the registrant becomes its owner).</summary>
    [Fact]
    public async Task TC_E12_AdminApprovesEstablishment_Activated()
    {
        var id = await SubmitForReviewAsync("CR-QA-E12");
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var approve = await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(EstablishmentStatus.Approved, await StatusOfAsync(id));
    }

    /// <summary>TC-E13: the admin rejects a PendingReview establishment with a
    /// reason → status Rejected (stays inactive).</summary>
    [Fact]
    public async Task TC_E13_AdminRejectsEstablishment_StaysInactive()
    {
        var id = await SubmitForReviewAsync("CR-QA-E13");
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var reject = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/reject",
            new { reason = "Authorization document is unclear." });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(EstablishmentStatus.Rejected, await StatusOfAsync(id));
    }

    // ===== §6.2 — Events ====================================================

    /// <summary>TC-E01 / BR-01: an approved organizer creates an event → 201.</summary>
    [Fact]
    public async Task TC_E01_OrganizerCreatesEvent()
    {
        var id = await ApproveEstablishmentAsync("CR-QA-E01");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(EventTypeId.ToString()), "step_one[type_uuid]" },
            { new StringContent("QA Gala Night"), "step_one[name]" },
            { new StringContent("An evening celebration event."), "step_one[description]" },
        };
        var resp = await owner.PostAsync($"/api/establishments/events?establishment_id={id}", form);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    /// <summary>
    /// TC-E02 — the doc says an operator cannot create events. The API has no
    /// operator/organizer gate on event creation (no CanManageEvents check), so
    /// this asserts the ENFORCED permission that does exist — a non-member cannot
    /// create an event on someone else's establishment (404). The operator-specific
    /// gate is a reported finding, not enforced here.
    /// </summary>
    [Fact]
    public async Task TC_E02_NonMemberCannotCreateEvent_OperatorGateIsAFinding()
    {
        var id = await ApproveEstablishmentAsync("CR-QA-E02");
        var stranger = _factory.CreateClientFor(Helpers.OtherUser);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(EventTypeId.ToString()), "step_one[type_uuid]" },
            { new StringContent("Intruder event"), "step_one[name]" },
            { new StringContent("Should not be allowed."), "step_one[description]" },
        };
        var resp = await stranger.PostAsync($"/api/establishments/events?establishment_id={id}", form);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // -- helpers --------------------------------------------------------------

    private async Task<EstablishmentStatus> StatusOfAsync(Guid id)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id)).Status;
    }

    private async Task<Guid> SubmitForReviewAsync(string cr)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await Helpers.CreateReadyToSubmitDraftAsync(_factory, creator, cr);
        (await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null))
            .EnsureSuccessStatusCode();
        return id;
    }

    private async Task<Guid> ApproveEstablishmentAsync(string cr)
    {
        var id = await SubmitForReviewAsync(cr);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        (await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null))
            .EnsureSuccessStatusCode();
        return id;
    }
}
