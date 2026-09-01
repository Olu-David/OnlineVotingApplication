namespace OnlineVotingApplication.DataTransferView
{
    public class CandidateVoteInput
    {
        public Guid CandidateId { get; set; }
        public string CandidateName { get; set; } = string.Empty;
        public int ManualVoteCount { get; set; }
    }
}
