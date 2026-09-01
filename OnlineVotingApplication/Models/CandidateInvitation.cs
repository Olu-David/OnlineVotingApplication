using System;

namespace OnlineVotingApplication.Models
{
    public class CandidateInvitation
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid? TenantId { get; set; }
        public virtual Tenant? Tenant { get; set; }

        public Guid ElectionEventId { get; set; }
        public virtual ElectionEvent? ElectionEvent { get; set; }

        public string? CandidateEmail { get; set; }
        public string Token { get; set; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        public bool IsUsed { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}