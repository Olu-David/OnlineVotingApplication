namespace OnlineVotingApplication.Models
{
    public class SupportTicket
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsResolved { get; set; } = false;
        public string? IpAdidress { get;  set; }
        public Guid? TenantId { get;  set; }
    }
}
