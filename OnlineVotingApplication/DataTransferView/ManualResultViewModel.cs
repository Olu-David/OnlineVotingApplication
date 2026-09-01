namespace OnlineVotingApplication.DataTransferView
{
    public class ManualResultViewModel
    {
        public Guid ElectionEventId { get; set; }
        public Guid PositionId { get; set; }
        public List<CandidateVoteInput> CandidateVotes { get; set; } = new List<CandidateVoteInput>();
    }
}
