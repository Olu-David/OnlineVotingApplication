using OnlineVotingApplication.Models;
using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    public class PositionDTO
    {
        public Guid? Id { get; set; }
        public string? PositionId { get; set; }
        public string? Name { get; set; }
        [Required]
        public int MaxChoice { get; set; } = 1;
        [Required]
        public Guid? ElectionId { get; set; }
        public Guid? CandidateId {  get; set; }

        public virtual ICollection<CandidateViewModel>? Candidates { get; set; }

    }
}
