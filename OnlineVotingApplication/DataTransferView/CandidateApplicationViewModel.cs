using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OnlineVotingApplication.DataTransferView
{
    public class CandidateApplicationViewModel
    {
        public Guid? ElectionEventId { get; set; }
        public Guid? TenantId { get; set; }

        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a position.")]
        public Guid? SelectedPositionId { get; set; }

        public IEnumerable<SelectListItem> PositionOptions { get; set; } = new List<SelectListItem>();
        public string? Title { get;  set; }
        public string? FullName { get;  set; }
        public string? CandidateEmail { get;  set; }
    }
}