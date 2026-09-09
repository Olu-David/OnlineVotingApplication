
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Models
{
    public class ElectionEvent
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int ElectionYear { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get;  set; }

        // 🔒 THE SHIELD: Changed from string to Enum. Must match the owner Tenant's Category.
        public TenantCategory Category { get; set; } = TenantCategory.Political;

        public Guid? TenantId { get; set; }
        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        public virtual ICollection<Positions>? Positions { get; set; }
        public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();
        public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
        public virtual ICollection<ElectionCustomField> CustomFields { get; set; } = new List<ElectionCustomField>();
        public virtual ICollection<CandidateInvitation> CandidateInvitations { get; set; } = new List<CandidateInvitation>();
        public DateTime CreatedAt { get;  set; }
        public bool IsDeleted { get;  set; }
        public string? ImageUrl { get; set; }
        public DateTime? DeletedAt { get;  set; }
    }
}
