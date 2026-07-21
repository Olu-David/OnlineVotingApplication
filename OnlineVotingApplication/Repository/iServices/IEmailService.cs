namespace OnlineVotingApplication.Repository.iServices
{
    public interface IEmailService
    {

        Task EmailSendAsync(string toEmail, string subject, string emailContent, CancellationToken cancellationToken = default);
    }
}
