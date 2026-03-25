using LMS_API.Services.Interfaces;
using System.Net;
using System.Net.Mail;

namespace LMS_API.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
    {
        var fromEmail = _config["EmailSettings:Email"];
        var appPassword = _config["EmailSettings:AppPassword"];

        if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(appPassword))
            throw new Exception("Email settings not configured");

        var smtp = new SmtpClient("smtp.gmail.com", 587)
        {
            Credentials = new NetworkCredential(fromEmail, appPassword),
            EnableSsl = true
        };

        var message = new MailMessage
        {
            From = new MailAddress(fromEmail, "LMS"),
            Subject = subject,
            Body = htmlContent,
            IsBodyHtml = true
        };

        message.To.Add(toEmail);

        try
        {
            await smtp.SendMailAsync(message);
            _logger.LogInformation("Email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email");
            throw;
        }
    }
}