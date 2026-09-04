using OnlineVotingApplication.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class Vote
    {
        [Key]
        public Guid Id { get; set; }

        public string? VoterId { get; set; }
        [ForeignKey(nameof(VoterId))]
        public ApplicationUser? Voter { get; set; }
        public Guid? CandidateId { get; set; }
        [ForeignKey(nameof(CandidateId))]
        public virtual Candidate Candidate { get; set; } = default!;
        public Guid? StateId { get; set; }
        [ForeignKey(nameof(StateId))]
         public virtual States State { get; set; } = default!;

        public Guid? ElectionId { get; set; }
        public Guid? PositionId { get; set; }
        public virtual Positions Positions { get; set; } = default!;
        public DateTime? CastAt { get; set; }
        public DateTime VotedAt { get;  set; }

        public Guid? TenantId { get; set; }
        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }
        public bool IsConfirmed { get;  set; }
        public bool HasVoted { get;  set; }
        public string? ConfirmationCode { get;  set; }
        public DateTime CreatedAt { get;  set; }
        [ForeignKey(nameof(ElectionId))]
        public virtual ElectionEvent? Election { get;  set; }
        public bool IsPenalized { get;  set; }
    }
}
