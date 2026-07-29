
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;


namespace OnlineVotingApplication.DataTransferView
{
    public class CandidateViewModel
    {
        public Guid CandidateID { get; set; }
        public string OfficialStaffId { get; set; } = default!;

        [Required(ErrorMessage = "Enter your Candidate Name ")]
        [StringLength(100, MinimumLength =6, ErrorMessage ="Name Length minimum is 6")]
        public string? Name { get; set; }
        [Required(ErrorMessage = "Enter your Candidate Manifesto ")]
        public string? Manifesto { get; set; }
        [Required(ErrorMessage = "Upload Picture of Candidate")]
        public IFormFile? CandidateImageUrl { get; set; }

        public string? StateName { get; set; }
        public string? image { get; set; }
        public string? Position { get; set; }
        public string? PartyName { get; set; }
        public IEnumerable<SelectListItem>? States {  get; set; }
        public IEnumerable<SelectListItem>? Lga { get; set; }
        public IEnumerable<SelectListItem>? Positions { get; set; }
        public Guid StateId { get; set; }
        public Guid PartyId { get; set; }
        public Guid PositonId { get; set; }
        public Guid LgaId { get; set; }
        public Guid PositionId { get; internal set; }
    }
}
