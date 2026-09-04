using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    public class SuperAdminCandidateCreationViewModel
    {
        [Required(ErrorMessage = "Please select a target tenant.")]
        public Guid TenantId { get; set; }

        [Required(ErrorMessage = "Please select an election event.")]
        public Guid ElectionEventId { get; set; }

        [Required(ErrorMessage = "Candidate email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        public string CandidateEmail { get; set; } = string.Empty;

        public Guid PositionId { get; set; }
        public string? Manifesto { get; set; }
        public IFormFile? CandidateImage { get; set; }

        // Optional Political Fields
        public Guid? PartyId { get; set; }
        public Guid? StateId { get; set; }
        public Guid? LgaId { get; set; }
    }
}