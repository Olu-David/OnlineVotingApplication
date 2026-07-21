using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Models
{
    public class PendingEmail
    {
        public int Id { get; set; }
        public string Recipient { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool IsSent { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int RetryCount { get;  set; }
        public virtual NotificationType Type { get; set; }
    }
}
