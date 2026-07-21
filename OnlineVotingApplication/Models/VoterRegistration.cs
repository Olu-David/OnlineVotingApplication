using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OnlineVotingApplication.Models
{
    public class VoterRegistration
    {
        public Guid Id {  get; set; }
        public Guid UserID { get; set; }
        public Guid ElectionId {  get; set; }
        public bool hasVoted { get; set; }
    }
}
