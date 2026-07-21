using OnlineVotingApplication.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class Candidate
    {
        public Guid Id { get; set; }
        public string?  CandidateID { get; set; }
        public string? Name { get; set; }
        public string? Manifesto { get; set; }
        public string? CandidateImg { get; set; }
        public bool isDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public bool isApproved {  get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid StateId { get; set; }
        [ForeignKey(nameof(StateId))]
        public virtual States State { get; set; } = null!;
        //Navigationn Properties
        public string UserId { get; set; } = string.Empty;
        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser? User { get; set; }  

        public Guid PartyId{ get; set; }
        [ForeignKey(nameof(PartyId))]
        public virtual Party Party { get; set; } = null!;
        public Guid PositionId{ get; set; }
        [ForeignKey(nameof(PositionId))]
        public  virtual Positions Position { get; set; } = null!;
        public Guid? LgaId {  get; set; }
        [ForeignKey(nameof(LgaId))]
        public virtual LGA LGA {  get; set; }=null!;
    }
}
