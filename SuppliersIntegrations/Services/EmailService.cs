using System;
using System.Net.Mail;
using System.Threading.Tasks;
using BOMVIEW.Models;
using System.Net;
using System.Collections.Generic;
using BOMVIEW.Interfaces;

namespace BOMVIEW.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger _logger;

        public EmailService(EmailSettings settings, ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SendEmailAsync(string subject, string body, List<string> attachmentPaths = null)
        {
            try
            {
                using var mailMessage = new MailMessage();
                mailMessage.From = new MailAddress(_settings.FromEmail);
                mailMessage.To.Add(_settings.ToEmail);
                mailMessage.Subject = subject;
                mailMessage.Body = body;
                mailMessage.IsBodyHtml = false;

                // Add attachments if any
                if (attachmentPaths != null)
                {
                    foreach (var path in attachmentPaths)
                    {
                        mailMessage.Attachments.Add(new Attachment(path));
                    }
                }

                using var smtpClient = new SmtpClient(_settings.SmtpServer, _settings.SmtpPort);
                smtpClient.EnableSsl = _settings.EnableSsl;
                smtpClient.Credentials = new NetworkCredential(_settings.FromEmail, _settings.Password);

                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogSuccess("Email sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending email: {ex.Message}");
                throw;
            }
        }
    }
}