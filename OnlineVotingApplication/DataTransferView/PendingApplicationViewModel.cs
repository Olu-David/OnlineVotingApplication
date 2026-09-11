namespace OnlineVotingApplication.DataTransferView
{
    public class PendingApplicationViewModel
    {
        public Guid? ElectionEventId { get; set; }
        public Guid? PositionId { get; set; }
        public string? CandidateEmail { get; set; }
        public string? CandidateName { get; set; }
        public string? ElectionTitle { get; set; }
        public string? PositionName { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? Id { get;  set; }
    }
}