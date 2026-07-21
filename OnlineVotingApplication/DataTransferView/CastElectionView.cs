using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.DataTransferView
{
    public class CastElectionView
    {

        public int ElectionId { get; set; }
        public int PositionId { get; set; }

        public List<Candidate> Candidates { get; set; } = null!;


        public int SelectedCandidateId { get; set; }
    }
}
