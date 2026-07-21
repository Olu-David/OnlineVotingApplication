namespace OnlineVotingApplication.Models
{
    public class VoteResultDto
    {
        public Guid CandidateId { get; set; } 
        public string CandidateName { get; set; } = string.Empty;
        public int VoteCount { get; set; }
    }
}


