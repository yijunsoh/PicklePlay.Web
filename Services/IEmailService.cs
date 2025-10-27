public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody, string? fromOverride = null, string? fromNameOverride = null);
}
