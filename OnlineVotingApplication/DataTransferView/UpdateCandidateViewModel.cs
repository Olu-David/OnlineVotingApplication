using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace OnlineVotingApplication.DataTransferView
{
    public class UpdateCandidateViewModel
    {
        public string OfficialStaffId { get; set; } = null!;
        public Guid CandidateID { get; set; }
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