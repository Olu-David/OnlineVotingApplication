using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class Party
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? LogoUrl { get; set; }
        public virtual ICollection<Candidate>? Candidates { get; set; }
        public bool IsDeleted { get;  set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? TenantId { get;  set; }
        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }
    }
}
