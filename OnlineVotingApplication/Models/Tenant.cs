using System;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Models
{
    public class Tenant
    {
        public Guid Id { get; set; }
        public string OrganizationName { get; set; } = string.Empty;

        // 🔒 THE RULE SETTER: Determines the fixed category for this platform client
        public TenantCategory TenantCategory { get; set; }

        public string Slug { get; set; } = string.Empty;
        public string SubscriptionPlan { get; set; } = "Free";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsApproved { get; set; }
        public bool IsActive { get; set; }
        public int MaxAllowedElections { get; set; }
        public string? ProfilePicture { get; set; }
    }
}
