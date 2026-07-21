using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Settings;

namespace OnlineVotingApplication.Repository.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _emailSettings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> emailSettings, ILogger<EmailService> logger)
        {
            _emailSettings = emailSettings.Value;
            _logger = logger;
        }

        public async Task EmailSendAsync(string toEmail, string subject, string emailContent, CancellationToken cancellationToken = default)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = emailContent };
            message.Body = bodyBuilder.ToMessageBody();

            // 'using' ensures client is disposed AND disconnected automatically
            using (var client = new SmtpClient())
            {
                try
                {
                    // Pass the token so connection cancels on shutdown
                    await client.ConnectAsync(_emailSettings.Host, _emailSettings.Port, SecureSocketOptions.StartTls, cancellationToken);
                    await client.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.Password, cancellationToken);
                    await client.SendAsync(message, cancellationToken);

                    _logger.LogInformation("Email sent successfully to {Recipient}", toEmail);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Email send canceled for {Recipient} due to shutdown.", toEmail);
                    throw; // Re-throw to let BackgroundService know it was canceled
                }
                catch (AuthenticationException ex)
                {
                    _logger.LogError(ex, "Authentication failed for {Recipient}", toEmail);
                    throw;
                }
                catch (SmtpCommandException ex)
                {
                    _logger.LogError(ex, "SMTP error for {Recipient}", toEmail);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error sending email to {Recipient}", toEmail);
                    throw;
                }
                finally
                {
                    if (client != null)
                    {
                        ((IDisposable)client).Dispose(); // C# automatically runs this!
                    }
                }
            }
        }
    }
}