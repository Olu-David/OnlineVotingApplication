namespace OnlineVotingApplication.Models
{
    public class Result
    {
        public Guid Id { get; set; }
        public Guid CandidateId { get; set; }
        public int TotalVotes {get; set;}
    }
}
