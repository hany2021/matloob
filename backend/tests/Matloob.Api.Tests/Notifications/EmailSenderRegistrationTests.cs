using Matloob.Api.Infrastructure.Notifications;
using Matloob.Api.Tests.Establishments;
using Matloob.Domain.Establishments;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Notifications;

/// <summary>
/// Branch 2 (email sender abstraction): the <see cref="IEmailSender"/> port is
/// registered and resolves to the no-op default. Nothing calls it yet — this
/// just guards the DI wire-up (mirrors the ISmsSender binding) so the invite
/// flow in Branch 4 has a sender to depend on.
/// </summary>
public sealed class EmailSenderRegistrationTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public EmailSenderRegistrationTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void IEmailSender_ResolvesToNoOpDefault()
    {
        using var scope = _factory.CreateDbScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        Assert.IsType<NoOpEmailSender>(sender);
    }

    [Fact]
    public async Task NoOpEmailSender_SendInvite_CompletesWithoutSending()
    {
        using var scope = _factory.CreateDbScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        // No throw, no external call — the no-op just logs and returns.
        await sender.SendInviteAsync(
            toEmail: "invitee@test",
            establishmentName: "Rotana",
            inviteUrl: "http://localhost:3001/ar/invite/tok",
            role: EstablishmentMemberRole.Manager,
            ct: default);
    }
}
