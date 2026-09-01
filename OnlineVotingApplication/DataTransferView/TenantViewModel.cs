using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.DataTransferView
{
    public class TenantViewModel
    {
        public Guid Id { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public TenantCategory TenantCategory { get; set; }
        public string Slug { get; set; } = string.Empty; // e.g., "lagos-state" or "abuja-uni"
        public string SubscriptionPlan { get; set; } = "Free";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; internal set; }
        public int MaxAllowedElections { get; internal set; }
    }
}
