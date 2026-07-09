using System.Net;
using System.Net.Mail;

namespace SpatialAI.Api.Email;

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendMagicLinkAsync(string toEmail, string toName, string link, CancellationToken ct);
}

/// <summary>
/// SMTP magic-link sender configured by <c>Email:{Host,Port,User,Pass,From}</c>. When SMTP is not
/// configured it logs the link instead of sending (dev convenience) — pair with
/// <c>Auth:RequireVerification=false</c> to skip email entirely if conference deliverability is risky.
/// </summary>
public sealed class SmtpEmailSender(IConfiguration cfg, ILogger<SmtpEmailSender> log) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(cfg["Email:Host"]);

    public async Task SendMagicLinkAsync(string toEmail, string toName, string link, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            log.LogWarning("Email not configured — magic link for {Email}: {Link}", toEmail, link);
            return;
        }

        var host = cfg["Email:Host"]!;
        var port = int.TryParse(cfg["Email:Port"], out var p) ? p : 587;
        var user = cfg["Email:User"];
        var pass = cfg["Email:Pass"];
        var from = cfg["Email:From"] ?? user ?? "no-reply@spatialai.local";

        using var client = new SmtpClient(host, port) { EnableSsl = true };
        if (!string.IsNullOrEmpty(user))
            client.Credentials = new NetworkCredential(user, pass);

        using var msg = new MailMessage(from, toEmail)
        {
            Subject = "Your SpatialAI workshop link",
            Body = $"Hi {toName},\n\nOpen your SpatialAI space:\n{link}\n\nThis link expires in 30 minutes.\n\n— SpatialAI",
            IsBodyHtml = false,
        };
        await client.SendMailAsync(msg, ct);
    }
}
