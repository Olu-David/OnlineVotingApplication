using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    public class CandidateViewModel
    {
        // ─── Identity / keys ────────────────────────────────────
        public Guid CandidateID { get; set; }
        public Guid CandidateInvitationID { get; set; }

        [Required]
        public Guid? ElectionEventId { get; set; }

        public Guid? PositionId { get; set; }
        public string? Position { get; set; }
        public Guid? StateId { get; set; }
        public Guid? LgaId { get; set; }
        public Guid? PartyId { get; set; }

        // ─── Core content ───────────────────────────────────────
        public string? Name { get; set; }
        public string? Manifesto { get; set; }

        // ✅ Profile picture — required (enforced in create action), ≤ 500 KB, image only
        [MaxFileSize(500 * 1024, ErrorMessage = "Profile picture must be under 500 KB.")]
        [AllowedImageTypes(".jpg", ".jpeg", ".png", ".webp",
            ErrorMessage = "Profile picture must be a JPG, PNG, or WEBP image.")]
        public IFormFile? CandidateImageUrl { get; set; }

        // ─── Server-locked display values ───────────────────────
        public string? LockedName { get; set; }
        public string? LockedPositionName { get; set; }
        public string? LockedElectionTitle { get; set; }
        public bool IsPolitical { get; set; }

        // ─── Display-only labels ────────────────────────────────
        public string? StateName { get; set; }
        public string? LgaName { get; set; }
        public string? PartyName { get; set; }
        public string? PositionName { get; set; }
        public string? image { get; set; }
        public int VoteCount { get; set; }

        // ─── Dropdown sources ───────────────────────────────────
        public IEnumerable<SelectListItem>? States { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem>? Lga { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem>? Positions { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem>? Parties { get; set; } = new List<SelectListItem>();

        // ─── Dynamic custom fields ──────────────────────────────
        public Dictionary<Guid, string> DynamicAnswers { get; set; } = new Dictionary<Guid, string>();
        public List<ElectionCustomField> CustomFields { get; set; } = new List<ElectionCustomField>();

        // ─── Uploads / gallery ──────────────────────────────────
        // ✅ Gallery — optional, each ≤ 1 MB, image only
        [MaxFileSize(1024 * 1024, ErrorMessage = "Each gallery image must be under 1 MB.")]
        [AllowedImageTypes(".jpg", ".jpeg", ".png", ".webp",
            ErrorMessage = "Gallery images must be JPG, PNG, or WEBP.")]
        public List<IFormFile> GalleryPhotos { get; set; } = new List<IFormFile>();

        public List<CandidateGallery>? ExistingGalleries { get; set; } = new();

        public bool IsApproved { get; set; }
        public string? CandidateImg { get; set; }
    }
}