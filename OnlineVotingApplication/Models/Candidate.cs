using OnlineVotingApplication.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class Candidate
    {
        public Guid Id { get; set; }
        public string? CandidateID { get; set; }
        public string? Name { get; set; }

        [Required]
        [MaxLength(150)]
        public string Slug { get; set; } = string.Empty;
        public string? Manifesto { get; set; }
        public string? CandidateImg { get; set; }
        public bool isDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public bool isApproved { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid? ElectionEventId { get; set; }
        [ForeignKey(nameof(ElectionEventId))]
        public virtual ElectionEvent? ElectionEvent { get; set; }

        public Guid? StateId { get; set; }
        [ForeignKey(nameof(StateId))]
        public virtual States? State { get; set; }

        public string? UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser? User { get; set; }

        public Guid? PartyId { get; set; }
        [ForeignKey(nameof(PartyId))]
        public virtual Party? Party { get; set; }

        public Guid? PositionId { get; set; }
        [ForeignKey(nameof(PositionId))]
        public virtual Positions? Position { get; set; }

        public Guid? LgaId { get; set; }
        [ForeignKey(nameof(LgaId))]
        public virtual LGA? LGA { get; set; }

        public Guid? TenantId { get; set; }
        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        public virtual ICollection<CandidateCustomValue> CustomValues { get; set; } = new List<CandidateCustomValue>();
        public virtual ICollection<CandidateGallery> GalleryPhotos { get; set; } = new List<CandidateGallery>();
        
        public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();

    }
}