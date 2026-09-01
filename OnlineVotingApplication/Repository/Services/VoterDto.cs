
namespace OnlineVotingApplication.Services
{
    public class VoterDto
    {
        public string? Id { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}