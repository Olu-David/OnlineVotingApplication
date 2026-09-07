using Microsoft.Extensions.Logging;
using Resend;
using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Repository.Services
{
    public class EmailService : IEmailService
    {
        private readonly IResend _resend;
        private readonly ILogger<EmailService> _logger;

        #region EmailService
        public EmailService(IResend resend, ILogger<EmailService> logger)
        {
            _resend = resend;
            _logger = logger;
        }
        #endregion

        #region EmailSendAsync
        public async Task EmailSendAsync(string toEmail, string subject, string emailContent, CancellationToken cancellationToken = default)
        {
            var message = new EmailMessage
            {
                From = "noreply@votezy.com.ng",
                To = { toEmail },
                Subject = subject,
                HtmlBody = emailContent
            };

            try
            {
                var response = await _resend.EmailSendAsync(message, cancellationToken);

                _logger.LogInformation("Email sent successfully to {Recipient}. Resend ID: {MessageId}", toEmail, response.Content.ToString());
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Email send canceled for {Recipient} due to shutdown.", toEmail);
                throw;
            }
            catch (ResendException ex)
            {
                _logger.LogError(ex, "Resend API error sending email to {Recipient}", toEmail);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending email to {Recipient}", toEmail);
                throw;
            }
        }
        #endregion
    }
}