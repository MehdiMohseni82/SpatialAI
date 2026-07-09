using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SpatialAI.Api.Email;

/// <summary>
/// Sends magic-link emails via Brevo's transactional REST API using an <c>xkeysib-…</c> API key
/// (Email:BrevoApiKey) — no separate SMTP key needed. Sender + domain must be authorised in Brevo,
/// and the server's egress IPs whitelisted under Brevo → Security → Authorised IPs.
/// </summary>
public sealed class BrevoEmailSender(IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<BrevoEmailSender> log) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(cfg["Email:BrevoApiKey"]);

    public async Task SendMagicLinkAsync(string toEmail, string toName, string link, CancellationToken ct)
    {
        var apiKey = cfg["Email:BrevoApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            log.LogWarning("Brevo not configured — magic link for {Email}: {Link}", toEmail, link);
            return;
        }

        var fromEmail = cfg["Email:From"] ?? "noreply@dotnet-talk.com";
        var fromName = cfg["Email:FromName"] ?? "SpatialAI";
        var payload = new
        {
            sender = new { email = fromEmail, name = fromName },
            to = new[] { new { email = toEmail, name = string.IsNullOrWhiteSpace(toName) ? toEmail : toName } },
            subject = "Your SpatialAI sign-in link",
            htmlContent =
                $"<p>Hi {System.Net.WebUtility.HtmlEncode(toName)},</p>" +
                $"<p>Open your SpatialAI space:</p>" +
                $"<p><a href=\"{link}\">{link}</a></p>" +
                $"<p>This link expires in 30 minutes.</p><p>— SpatialAI</p>",
        };

        var client = httpFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.TryAddWithoutValidation("api-key", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = JsonContent.Create(payload);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogError("Brevo send failed {Status} for {Email}: {Body}", (int)resp.StatusCode, toEmail, body);
            throw new InvalidOperationException($"Brevo send failed: {(int)resp.StatusCode}");
        }
        log.LogInformation("Sent magic-link email to {Email} via Brevo.", toEmail);
    }
}
