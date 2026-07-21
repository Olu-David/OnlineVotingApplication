using Microsoft.CodeAnalysis.Elfie.Diagnostics;

namespace OnlineVotingApplication.Models
{
    public class Election
    {
        public Guid Id { get; set; }
        public string? TItle { get; set; }
        public int ElectionYear { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public virtual ICollection<Positions>? Positions { get; set; }
        public bool IsActive { get; internal set; }
        public ICollection<Vote> Votes { get; set; } = null!;


    }
}
