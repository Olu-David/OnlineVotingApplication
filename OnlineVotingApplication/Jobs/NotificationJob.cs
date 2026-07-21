using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Jobs
{


    public record NotificationJob(string To, string Subject, string Body, NotificationType Type);

}
