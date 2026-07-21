namespace OnlineVotingApplication.Models
{
    public class AuditLog
    {
        public Guid Id {  get; set; }
        public string? Action { get; set; }
        public Guid UserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
