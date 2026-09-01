namespace OnlineVotingApplication.DataTransferView
{
    public class PenalizedVoterDto
    {
        public Guid? VoterId { get; set; }
        public string? Id { get; set; }
        public string? VoterEmail { get; set; }
        public string? VoterName { get; set; }
        public Guid? ElectionId { get; set; }
        public string? ElectionTitle { get; set; }
        public string? Reason { get; set; }
        public DateTime PenalizedAt { get; set; }
    }
}