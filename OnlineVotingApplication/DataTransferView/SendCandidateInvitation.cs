namespace OnlineVotingApplication.DataTransferView
{
    public class SendCandidateInvitation
    {

        public Guid? ElectionEventId { get; set; }
        public string? CandidateEmail { get; set; }
        public string? SecureLink { get; set; }
        public string? Token { get; set; }

    }
}
