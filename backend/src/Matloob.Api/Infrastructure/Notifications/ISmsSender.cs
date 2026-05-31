using Microsoft.Extensions.Logging;

namespace Matloob.Api.Infrastructure.Notifications;

/// <summary>
/// Outbound SMS port. The legacy app delivered some notifications (e.g. the
/// new-offer alert, which had no database channel) only over SMS. The
/// abstraction is ported so the notification fanout keeps the <c>sms</c>
/// channel wired; a real provider is intentionally NOT configured yet
/// (Q-NOTIF-TRANSPORT). <see cref="NoOpSmsSender"/> is the default binding.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string recipient, string message, CancellationToken ct);
}

/// <summary>
/// Default <see cref="ISmsSender"/>: logs the intent and returns. Keeps the
/// SMS channel in the fanout so SMS-only notifications are not silently
/// dropped, without sending anything until a real provider is wired.
/// </summary>
public sealed class NoOpSmsSender : ISmsSender
{
    private readonly ILogger<NoOpSmsSender> _logger;

    public NoOpSmsSender(ILogger<NoOpSmsSender> logger) => _logger = logger;

    public Task SendAsync(string recipient, string message, CancellationToken ct)
    {
        _logger.LogInformation(
            "SMS (no-op) to {Recipient}: {Message}", recipient, message);
        return Task.CompletedTask;
    }
}
