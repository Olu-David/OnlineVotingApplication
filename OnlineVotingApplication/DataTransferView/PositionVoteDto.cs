namespace OnlineVotingApplication.DataTransferView
{
    public class PositionVoteDto
    {
        public string PositionName { get; set; } = string.Empty;
        public List<CandidateVoteDto> candidateVotes { get; set; } = null!;

    }
}
