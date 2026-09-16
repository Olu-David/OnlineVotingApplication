using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
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

        [Required(ErrorMessage = "Please select a position.")]
        public Guid PositionId { get; set; }

        [MaxLength(2000, ErrorMessage = "Manifesto cannot exceed 2000 characters.")]
        public string? Manifesto { get; set; }

        // ✅ Profile image is REQUIRED
        [Required(ErrorMessage = "Profile picture is required.")]
        public IFormFile CandidateImage { get; set; } = default!;

        // Optional Political Fields
        public Guid? PartyId { get; set; }
        public Guid? StateId { get; set; }
        public Guid? LgaId { get; set; }

        // ✅ Gallery is OPTIONAL
        public List<IFormFile>? GalleryPhotos { get; set; } = new List<IFormFile>();
    }
}