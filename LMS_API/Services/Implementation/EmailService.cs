using LMS_API.Services.Interfaces;
using SendGrid;
using SendGrid.Helpers.Mail;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
    {
        var client = new SendGridClient(_config["SendGrid:ApiKey"]);

        var from = new EmailAddress(
            _config["SendGrid:FromEmail"],
            _config["SendGrid:FromName"]
        );

        var to = new EmailAddress(toEmail);

        var msg = MailHelper.CreateSingleEmail(from, to, subject, "", htmlContent);

        await client.SendEmailAsync(msg);
    }
}