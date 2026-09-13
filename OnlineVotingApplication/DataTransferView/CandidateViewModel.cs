using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnlineVotingApplication.Models;
using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    // 💡 Refactored from 'record' to 'class' so form model binding works.
    public class CandidateViewModel
    {
        // ─── Identity / keys ────────────────────────────────────
        public Guid CandidateID { get; set; }

        [Required]
        public Guid? ElectionEventId { get; set; }

        public Guid? PositionId { get; set; }
        public string? Position {  get; set; }
        public Guid? StateId { get; set; }
        public Guid? LgaId { get; set; }
        public Guid? PartyId { get; set; }

        // ─── Core content ───────────────────────────────────────
        public string? Name { get; set; }
        public string? Manifesto { get; set; }
        public IFormFile? CandidateImageUrl { get; set; }

        // ─── Server-locked display values (never trust the client) ──
        public string? LockedName { get; set; }
        public string? LockedPositionName { get; set; }
        public string? LockedElectionTitle { get; set; }
        public bool IsPolitical { get; set; }

        // ─── Display-only labels (for confirmation / receipts) ──
        public string? StateName { get; set; }
        public string? LgaName { get; set; }
        public string? PartyName { get; set; }
        public string? PositionName { get; set; }
        public string? image { get; set; }
        public int VoteCount { get; set; }

        // ─── Dropdown sources (strongly typed, used by the view) ──
        public IEnumerable<SelectListItem>? States { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem>? Lga { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem>? Positions { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem>? Parties { get; set; } = new List<SelectListItem>();

        // ─── Dynamic custom fields ──────────────────────────────
        public Dictionary<Guid, string> DynamicAnswers { get; set; } = new Dictionary<Guid, string>();
        public List<ElectionCustomField> CustomFields { get; set; } = new List<ElectionCustomField>();

        // ─── Uploads / gallery ──────────────────────────────────
        public List<IFormFile>? GalleryPhotos { get; set; } = new List<IFormFile>();
        public ICollection<CandidateGallery>? GalleryPhotoss { get; set; }
    }
}