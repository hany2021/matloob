using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Establishments;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Users;

/// <summary>
/// AddMember sec 422 user_not_found_in_system path. The endpoint refuses to
/// bind a userId that doesn't exist as a row in the local users table
/// (spec sec 6.3) -- a user who has never logged in cannot be added.
/// </summary>
public sealed class MemberAddValidationTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public MemberAddValidationTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<Guid> CreateApprovedEstablishmentAsync(string crNumber)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: crNumber);
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);
        await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null);
        return id;
    }

    [Fact]
    public async Task AddMember_UnknownUserId_Returns422()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-VAL-MISSING");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = "never-logged-in-user", role = "HR" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("user_not_found_in_system",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddMember_InactiveLocalUser_Returns422()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-VAL-INACTIVE");

        // Seed an inactive user row.
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deactivated = User.CreateFromIdentity(
                id: Guid.NewGuid(),
                identityId: "deactivated-user-1",
                email: null, name: null, phone: null,
                firstSeenAt: DateTimeOffset.UtcNow);
            deactivated.Deactivate();
            db.Users.Add(deactivated);
            await db.SaveChangesAsync();
        }

        var owner = _factory.CreateClientFor(Helpers.Creator);
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = "deactivated-user-1", role = "HR" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_ExistingActiveUser_Succeeds()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-VAL-OK");

        await Helpers.SeedLocalUserAsync(_factory, "real-existing-user");

        var owner = _factory.CreateClientFor(Helpers.Creator);
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = "real-existing-user", role = "Manager" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
