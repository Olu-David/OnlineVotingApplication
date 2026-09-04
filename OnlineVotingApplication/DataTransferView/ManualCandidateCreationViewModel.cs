
namespace OnlineVotingApplication.DataTransferView
{
    public class ManualCandidateCreationViewModel
    {
        public Guid ElectionEventId { get;  set; }
        public string CandidateEmail { get; set; } = string.Empty;
        public IFormFile? CandidateImage { get;  set; }
        public string? Manifesto { get;  set; }
        public Guid PositionId { get;  set; }
        public Guid? PartyId { get;  set; }
        public Guid? StateId { get;  set; }
        public Guid? LgaId { get; internal set; }
    }
}