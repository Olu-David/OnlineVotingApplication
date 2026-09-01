namespace OnlineVotingApplication.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public string? UserId { get; set; }           // Who performed the action
        public string? Action { get; set; }           // What type of action (e.g., "Candidate Deleted", "Settings Updated")
        public string? Details { get; set; }          // Specific description of what changed
        public string? IpAddress { get; set; }        // The IP address the request came from
        public Guid? TenantId { get; set; }          // Optional: Scoped to a specific election/tenant
        public DateTime Timestamp { get; set; } = DateTime.UtcNow; // When it happened}
    }
}
