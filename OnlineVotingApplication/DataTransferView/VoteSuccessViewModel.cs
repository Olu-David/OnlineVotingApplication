namespace OnlineVotingApplication.DataTransferView
{
    public class VoteSuccessViewModel
    {
      

        public string? ElectionTitle { get;  set; }
        public string? PositionName { get;  set; }
        public string? CandidateName { get;  set; }
        public DateTime RecordedAt { get;  set; }
    }
}