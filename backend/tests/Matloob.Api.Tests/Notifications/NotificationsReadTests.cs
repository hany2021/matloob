using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Api.Tests.Establishments;
using Matloob.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Notifications;

/// <summary>
/// Tests for the DB-backed notification read endpoints (list / unread-count /
/// mark-as-read). Notifications are seeded directly; each test uses a distinct
/// recipient id to stay isolated within the shared fixture DB.
/// </summary>
public sealed class NotificationsReadTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public NotificationsReadTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedAsync(NotificationRecipientType type, string recipientId, bool read)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = new Notification(
            Guid.NewGuid(), type, recipientId, NotificationTypes.SentOffer,
            "A worker", "Accepted your offer.", "offers", Guid.NewGuid().ToString());
        if (read) n.MarkRead(DateTimeOffset.UtcNow);
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Notifications_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/users/notifications")).StatusCode);
    }

    [Fact]
    public async Task UserList_ReturnsSeeded_WithPaginationEnvelope()
    {
        var sub = "notif-list-user";
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        await SeedAsync(NotificationRecipientType.User, sub, read: true);

        var client = _factory.CreateClientFor(new TestUser(sub, new[] { "matloob_user" }));
        var resp = await client.GetAsync("/api/users/notifications");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("data").GetArrayLength());
        Assert.Equal(2, root.GetProperty("meta").GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("meta").GetProperty("current_page").GetInt32());
        Assert.True(root.TryGetProperty("links", out _));
        // One item carries the expected snake_case shape.
        var item = root.GetProperty("data")[0];
        Assert.Equal("sent_offer", item.GetProperty("type").GetString());
        Assert.True(item.TryGetProperty("is_read", out _));
    }

    [Fact]
    public async Task UserUnreadCount_CountsUnreadOnly()
    {
        var sub = "notif-count-user";
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        await SeedAsync(NotificationRecipientType.User, sub, read: true);

        var client = _factory.CreateClientFor(new TestUser(sub, new[] { "matloob_user" }));
        using var doc = JsonDocument.Parse(
            await (await client.GetAsync("/api/users/notifications/unread-count"))
                .Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task UserList_OnlyUnreadFilter()
    {
        var sub = "notif-onlyunread-user";
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        await SeedAsync(NotificationRecipientType.User, sub, read: true);

        var client = _factory.CreateClientFor(new TestUser(sub, new[] { "matloob_user" }));
        using var doc = JsonDocument.Parse(
            await (await client.GetAsync("/api/users/notifications?only_unread=true"))
                .Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task MarkAllAsRead_ClearsUnreadCount()
    {
        var sub = "notif-markall-user";
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        var client = _factory.CreateClientFor(new TestUser(sub, new[] { "matloob_user" }));

        var mark = await client.PostAsJsonAsync(
            "/api/users/notifications/mark-as-read", new { });
        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        using var doc = JsonDocument.Parse(
            await (await client.GetAsync("/api/users/notifications/unread-count"))
                .Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task MarkSingleAsRead_LeavesOthersUnread()
    {
        var sub = "notif-marksingle-user";
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        await SeedAsync(NotificationRecipientType.User, sub, read: false);
        var client = _factory.CreateClientFor(new TestUser(sub, new[] { "matloob_user" }));

        // Grab one notification's id from the list.
        Guid firstId;
        using (var listDoc = JsonDocument.Parse(
            await (await client.GetAsync("/api/users/notifications")).Content.ReadAsStringAsync()))
        {
            firstId = listDoc.RootElement.GetProperty("data")[0].GetProperty("id").GetGuid();
        }

        await client.PostAsJsonAsync("/api/users/notifications/mark-as-read", new { id = firstId.ToString() });

        using var doc = JsonDocument.Parse(
            await (await client.GetAsync("/api/users/notifications/unread-count"))
                .Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task EstablishmentList_ReturnsSeededForResolvedEstablishment()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var estId = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-NOTIF-EST");
        await creator.PostAsync($"/api/v1/establishments/registration/{estId}/submit", content: null);
        (await admin.PostAsync($"/api/v1/admin/establishments/{estId}/approve", content: null))
            .EnsureSuccessStatusCode();

        await SeedAsync(NotificationRecipientType.Establishment, estId.ToString(), read: false);

        var resp = await creator.GetAsync(
            $"/api/establishments/notifications?establishment_id={estId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetArrayLength());
    }
}
