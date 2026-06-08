using System.Net;
using System.Net.Mail;
using System.Text;
using Matloob.Domain.Establishments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Notifications;

/// <summary>
/// Bound to the <c>EmailConfiguration</c> section. When
/// <see cref="IsConfigured"/> is true the API uses <see cref="SmtpEmailSender"/>;
/// otherwise it falls back to <see cref="NoOpEmailSender"/> (see Program.cs).
/// </summary>
public sealed class EmailConfiguration
{
    public string? From { get; set; }
    public string? SmtpServer { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>STARTTLS — required by Office365 on port 587. Default true.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>Optional human display name on the From header.</summary>
    public string? FromName { get; set; } = "منصة مطلوب";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SmtpServer) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>
/// SMTP-backed <see cref="IEmailSender"/> (e.g. Office365 on
/// <c>smtp.office365.com:587</c> with STARTTLS). Sends the establishment-employee
/// invitation email. Delivery is <b>best-effort</b>: a send failure is logged
/// but does NOT fail the invite request — the invitation row + link already
/// exist, so the owner can resend.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailConfiguration _cfg;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailConfiguration> cfg, ILogger<SmtpEmailSender> logger)
    {
        _cfg = cfg.Value;
        _logger = logger;
    }

    public async Task SendInviteAsync(
        string toEmail,
        string establishmentName,
        string inviteUrl,
        EstablishmentMemberRole role,
        CancellationToken ct)
    {
        var subject = $"دعوة للانضمام إلى {establishmentName} على منصة مطلوب";

        using var message = new MailMessage
        {
            From = new MailAddress(_cfg.From!, _cfg.FromName ?? _cfg.From),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = BuildHtmlBody(establishmentName, RoleLabel(role), inviteUrl),
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(toEmail));

        using var client = new SmtpClient(_cfg.SmtpServer, _cfg.Port)
        {
            EnableSsl = _cfg.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(_cfg.Username, _cfg.Password),
        };

        try
        {
            await client.SendMailAsync(message, ct);
            _logger.LogInformation(
                "[INVITE-EMAIL] sent to={Email} establishment={Name} role={Role}",
                toEmail, establishmentName, role);
        }
        catch (Exception ex)
        {
            // Best-effort: don't 500 the invite because delivery failed.
            _logger.LogError(ex,
                "[INVITE-EMAIL] FAILED to send to={Email} establishment={Name}. Invite URL (recoverable): {Url}",
                toEmail, establishmentName, inviteUrl);
        }
    }

    private static string RoleLabel(EstablishmentMemberRole role) => role switch
    {
        EstablishmentMemberRole.Owner => "مالك",
        EstablishmentMemberRole.Manager => "مدير",
        EstablishmentMemberRole.HR => "موارد بشرية",
        EstablishmentMemberRole.Accountant => "محاسب",
        EstablishmentMemberRole.Commissioner => "مفوض",
        _ => "موظف",
    };

    private static string BuildHtmlBody(string establishmentName, string roleLabel, string inviteUrl)
    {
        var safeName = WebUtility.HtmlEncode(establishmentName);
        var safeRole = WebUtility.HtmlEncode(roleLabel);
        var safeUrl = WebUtility.HtmlEncode(inviteUrl);

        return $"""
            <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;color:#1f2937;max-width:560px;margin:auto;">
              <h2 style="color:#0f1727;">دعوة للانضمام إلى {safeName}</h2>
              <p>تمت دعوتك للانضمام إلى منشأة <strong>{safeName}</strong> على منصة <strong>مطلوب</strong> بصفة <strong>{safeRole}</strong>.</p>
              <p>للقبول، اضغط على الزر التالي وسجّل الدخول بنفس البريد الإلكتروني الذي وصلتك عليه هذه الرسالة:</p>
              <p style="text-align:center;margin:28px 0;">
                <a href="{safeUrl}" style="background:#0f1727;color:#fff;text-decoration:none;padding:12px 28px;border-radius:6px;display:inline-block;">قبول الدعوة</a>
              </p>
              <p style="color:#6b7280;font-size:13px;">إذا لم يعمل الزر، انسخ الرابط التالي والصقه في المتصفح:<br>
                <span dir="ltr">{safeUrl}</span></p>
              <p style="color:#9ca3af;font-size:12px;">هذه الدعوة صالحة لمدة 7 أيام. إذا لم تكن تتوقع هذه الرسالة، يمكنك تجاهلها.</p>
            </div>
            """;
    }
}
