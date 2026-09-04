namespace OnlineVotingApplication.DataTransferView
{
    public class VoterPenalizationStatusDto
    {
        public string? Id { get; set; }
        public string? VoterEmail { get; set; }
        public string? VoterName { get; set; }
        public Guid ElectionId { get; set; }
        public bool IsPenalized { get; internal set; }
    }
}