using System.Net;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Establishments;

namespace Matloob.Api.Tests.Common;

/// <summary>
/// Focused tests for the global response-envelope shim (<c>ResponseEnvelopeShim</c>,
/// wired as the FastEndpoints <c>ResponseSerializer</c>). Verifies the four
/// behaviours the shim guarantees:
/// <list type="bullet">
///   <item>a bare single object becomes <c>{ data: {...} }</c>;</item>
///   <item>a bare list becomes <c>{ data: [...], meta, links }</c> with the exact
///   Laravel meta/links field names;</item>
///   <item>DTOs marked <c>IBypassEnvelope</c> (notifications <c>unread-count</c>)
///   stay bare;</item>
///   <item>an already-enveloped DTO (the profile <c>DataEnvelope</c>) is not
///   double-wrapped.</item>
/// </list>
/// </summary>
public sealed class ResponseEnvelopeTests
    : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;

    private static readonly TestUser User = new(
        Sub: "envelope-shim-user",
        Roles: new[] { "matloob_user" });

    public ResponseEnvelopeTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => Helpers.SeedLocalUserAsync(_factory, User.Sub);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SingleObject_IsWrappedInDataEnvelope()
    {
        var client = _factory.CreateClientFor(User);

        // /api/v1/profile already returns DataEnvelope in-code; use a route that
        // returns a bare single object instead — the user profile read.
        var response = await client.GetAsync("/api/users/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("data", out var data));
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        // The profile payload lives under data (not double-wrapped: data.data absent).
        Assert.False(data.TryGetProperty("data", out _));
        Assert.Equal(User.Sub, data.GetProperty("identity_id").GetString());
    }

    [Fact]
    public async Task List_IsWrappedWithDataMetaLinks_LaravelFieldNames()
    {
        var client = _factory.CreateClientFor(User);

        // establishment-list returns a bare array → list envelope.
        var response = await client.GetAsync("/api/users/profile/establishment-list");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("data").ValueKind);

        // meta: exact Laravel snake_case field names the frontend depends on.
        var meta = root.GetProperty("meta");
        foreach (var key in new[]
                 { "current_page", "from", "last_page", "links", "path", "per_page", "to", "total" })
        {
            Assert.True(meta.TryGetProperty(key, out _), $"meta.{key} missing");
        }
        Assert.Equal(1, meta.GetProperty("current_page").GetInt32());
        Assert.Equal(1, meta.GetProperty("last_page").GetInt32());

        // links: first/last/prev/next.
        var links = root.GetProperty("links");
        foreach (var key in new[] { "first", "last", "prev", "next" })
        {
            Assert.True(links.TryGetProperty(key, out _), $"links.{key} missing");
        }
    }

    [Fact]
    public async Task BypassMarkedResponse_StaysBare()
    {
        var client = _factory.CreateClientFor(User);

        // unread-count is IBypassEnvelope → frontend reads { count } directly.
        var response = await client.GetAsync("/api/users/notifications/unread-count");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.TryGetProperty("data", out _));
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task AlreadyEnvelopedResponse_IsNotDoubleWrapped()
    {
        var client = _factory.CreateClientFor(User);

        // The notifications feed already returns a { data, meta, links } envelope
        // (PaginationEnvelope : IBypassEnvelope) — the shim must not re-wrap it.
        var response = await client.GetAsync("/api/users/notifications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        // Not double-wrapped: data is the array itself, not another { data } object.
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("meta", out _));
    }

    [Fact]
    public async Task ErrorResponse_IsNotWrapped()
    {
        var anon = _factory.CreateClientFor(null);

        // 401 flows through the auth pipeline / ProblemDetails, never the
        // success serializer — must not gain a { data } wrapper.
        var response = await anon.GetAsync("/api/users/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            Assert.False(doc.RootElement.TryGetProperty("data", out _));
        }
    }
}
