using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class LGA
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;

        public Guid StateId { get; set; }
        [ForeignKey(nameof(Id))]
        public virtual States State { get; set; }=default!;
        public DateTime DeletedAt { get; internal set; }
        public bool IsDeleted { get; internal set; }

        public virtual ICollection<Candidate> Candidates { get;  set; }= new List<Candidate>();
    }
}
