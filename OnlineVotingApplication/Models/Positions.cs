using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class Positions
    {
        public Guid Id {  get; set; }
        public string? Name { get; set; }
        [Required]
        //public int MaxChoice { get; set; } = 1;
        //public Guid ElectionId {  get; set; }
        //[ForeignKey(nameof(ElectionId))]
        //public virtual Election? Election { get; set; }

        public virtual ICollection<Candidate>? Candidates { get; set; }
        public bool IsDeleted { get;  set; }
        public DateTime DeletedAt { get;  set; }
       
    }
}
