
namespace OnlineVotingApplication.DataTransferView
{
    public class StateResultDto
    {
        public string StateName { get; set; } = string.Empty;
        public List<PositionVoteDto> Positions { get; set; } = null!;
        public Guid? StateId { get;  set; }
        public int TotalVotes { get;  set; }
        public Guid? State { get;  set; }
        public Guid? CandidateId { get;  set; }
        public int VoteCount { get;  set; }
    }
}
