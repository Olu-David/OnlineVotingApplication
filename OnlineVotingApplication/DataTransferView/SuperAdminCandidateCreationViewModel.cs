using Microsoft.AspNetCore.Http;
using OnlineVotingApplication.Validation;
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

        // ✅ Profile image is REQUIRED — ≤ 500 KB, JPG/PNG/WEBP only
        [Required(ErrorMessage = "Profile picture is required.")]
        [MaxFileSize(500 * 1024, ErrorMessage = "Profile picture must be under 500 KB.")]
        [AllowedImageTypes(".jpg", ".jpeg", ".png", ".webp",
            ErrorMessage = "Profile picture must be a JPG, PNG, or WEBP image.")]
        public IFormFile CandidateImage { get; set; } = default!;

        // Optional Political Fields
        public Guid? PartyId { get; set; }
        public Guid? StateId { get; set; }
        public Guid? LgaId { get; set; }

        // ✅ Gallery is OPTIONAL — each ≤ 1 MB, JPG/PNG/WEBP only
        [MaxFileSize(1024 * 1024, ErrorMessage = "Each gallery image must be under 1 MB.")]
        [AllowedImageTypes(".jpg", ".jpeg", ".png", ".webp",
            ErrorMessage = "Gallery images must be JPG, PNG, or WEBP.")]
        public List<IFormFile>? GalleryPhotos { get; set; } = new List<IFormFile>();
    }
}