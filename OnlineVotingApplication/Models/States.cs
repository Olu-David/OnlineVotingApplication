using NuGet.Packaging.Core;
using OnlineVotingApplication.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class States
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = null!;
        public ICollection<LGA> Lgas { get; set; } = new List<LGA>();
        public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
        public ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

    }
}
