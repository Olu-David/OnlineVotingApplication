namespace OnlineVotingApplication.DataTransferView
{
    public class UpdateCandidateViewModel
    {
        public string OfficialStaffId { get; set; } = null!;
        public Guid CandidateID { get;  set; }
        public IFormFile? CandidateImageUrl { get;  set; }
        public string? Name { get;  set; }
        public string? Manifesto { get;  set; }
        public Guid? PartyId { get;  set; }
        public Guid? PositonId { get;  set; }
        public Guid? StateId { get;  set; }
    }
}