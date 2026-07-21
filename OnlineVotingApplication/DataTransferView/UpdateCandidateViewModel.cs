namespace OnlineVotingApplication.DataTransferView
{
    public class UpdateCandidateViewModel
    {
        public string OfficialStaffId { get; set; } = null!;
        public Guid CandidateID { get;  set; }
        public IFormFile? CandidateImageUrl { get;  set; }
        public string? Name { get; internal set; }
        public string? Manifesto { get; internal set; }
        public Guid PartyId { get; internal set; }
        public Guid PositonId { get; internal set; }
        public Guid StateId { get; internal set; }
    }
}