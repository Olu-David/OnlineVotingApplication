using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class CandidateGallery
    {
        public Guid Id { get; set; }

        public Guid CandidateId { get; set; }
        [ForeignKey(nameof(CandidateId))]
        public virtual Candidate Candidate { get; set; } = null!;

        public string ImageUrl { get; set; } = string.Empty; // Web relative file upload path
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public Guid? TenantId { get;  set; }
        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }
    }
}
