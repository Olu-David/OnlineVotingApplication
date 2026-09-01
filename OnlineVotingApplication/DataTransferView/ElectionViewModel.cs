using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.DataTransferView
{
    public class ElectionViewModel
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public int ElectionYear { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public TenantCategory Category { get; set; }
        public Guid? TenantId { get; set; }
        public string? TenantName { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
