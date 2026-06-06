using Matloob.Domain.Establishments;
using Microsoft.Extensions.Logging;

namespace Matloob.Api.Infrastructure.Notifications;

/// <summary>
/// Outbound email port for establishment-employee invitations. Mirrors
/// <see cref="ISmsSender"/>: the abstraction is wired so the invite flow can
/// "send" a link, but a real provider (SES or equivalent) is intentionally
/// NOT configured yet — <see cref="NoOpEmailSender"/> is the default binding
/// and only logs. See the plan's "out of scope: real email provider".
/// </summary>
public interface IEmailSender
{
    Task SendInviteAsync(
        string toEmail,
        string establishmentName,
        string inviteUrl,
        EstablishmentMemberRole role,
        CancellationToken ct);
}

/// <summary>
/// Default <see cref="IEmailSender"/>: logs the invite intent (including the
/// raw invite URL, so the link is recoverable from the API console during dev
/// QA) and returns. Sends nothing until a real provider is wired.
/// </summary>
public sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger) => _logger = logger;

    public Task SendInviteAsync(
        string toEmail,
        string establishmentName,
        string inviteUrl,
        EstablishmentMemberRole role,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[INVITE] email={Email} url={Url} role={Role} establishment={Name}",
            toEmail, inviteUrl, role, establishmentName);
        return Task.CompletedTask;
    }
}
