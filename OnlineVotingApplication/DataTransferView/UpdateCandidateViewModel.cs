using Microsoft.AspNetCore.Http;
using OnlineVotingApplication.Validation;
using System;
using System.Collections.Generic;

namespace OnlineVotingApplication.DataTransferView
{
    public class UpdateCandidateViewModel
    {
        public string OfficialStaffId { get; set; } = null!;

        public Guid CandidateID { get; set; }

        // ✅ Profile image is OPTIONAL on update — replace only if a new file is uploaded
        [MaxFileSize(500 * 1024, ErrorMessage = "Profile picture must be under 500 KB.")]
        [AllowedImageTypes(".jpg", ".jpeg", ".png", ".webp",
            ErrorMessage = "Profile picture must be a JPG, PNG, or WEBP image.")]
        public IFormFile? CandidateImageUrl { get; set; }

        public string? Name { get; set; }
        public string? Manifesto { get; set; }

        public Guid? PartyId { get; set; }
        public Guid? PositonId { get; set; }
        public Guid? StateId { get; set; }

        // Dynamic custom field answers (Key: CustomField Id, Value: Answer/Text/Selected Choice)
        public Dictionary<Guid, string> DynamicAnswers { get; set; } = new();

        // Dynamic uploaded files for custom fields (Key: CustomField Id, Value: Uploaded File)
        public Dictionary<Guid, IFormFile> DynamicFiles { get; set; } = new();
    }
}