using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

public class EmailSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSSL { get; set; } = true;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "PicklePlay";
}

public class EmailService : IEmailService
{
    private readonly EmailSettings _cfg;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> cfg, ILogger<EmailService> logger)
    {
        _cfg = cfg.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string? fromOverride = null, string? fromNameOverride = null)
    {
        using var client = new SmtpClient(_cfg.Host, _cfg.Port)
        {
            EnableSsl = _cfg.EnableSSL,
            Credentials = new NetworkCredential(_cfg.UserName, _cfg.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        var fromAddress = new MailAddress(fromOverride ?? _cfg.From, fromNameOverride ?? _cfg.FromName);
        using var msg = new MailMessage
        {
            From = fromAddress,
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        msg.To.Add(to);

        try
        {
            await client.SendMailAsync(msg);
            _logger.LogInformation("Email sent to {Email}", to);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP send failed to {Email}. Host={Host}, Port={Port}, SSL={SSL}", to, _cfg.Host, _cfg.Port, _cfg.EnableSSL);
            throw; // bubble up so caller can rollback user creation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed to {Email}", to);
            throw;
        }
    }
}
