using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnlineVotingApplication.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    // 💡 REFACTORED: Changed from 'record' to 'class' to support seamless Form Model Binding
    public class CandidateViewModel
    {
        public Guid CandidateID { get; set; }
        public string? Name { get; set; }
        public string? Manifesto { get; set; }
        public IFormFile? CandidateImageUrl { get; set; }

        public string? StateName { get; set; }
        public string? LgaName { get; set; }
        public string? image { get; set; }
        public string? Position { get; set; }
        public string? PartyName { get; set; }
        public IEnumerable<SelectListItem>? States { get; set; }
        public IEnumerable<SelectListItem>? Lga { get; set; }
        public IEnumerable<SelectListItem>? Positions { get; set; }

        public Guid? StateId { get; set; }
        public Guid? PartyId { get; set; }
        public Guid? PositionId { get; set; }
        public Guid? LgaId { get; set; }

        [Required]
        public Guid? ElectionEventId { get; set; }

        // 🌟 Dynamic dictionary mapping: Question Guid -> Answer string text
        public Dictionary<Guid, string> DynamicAnswers { get; set; } = new Dictionary<Guid, string>();

        // 🌟 Gallery Upload list binding parameter
        public List<IFormFile>? GalleryPhotos { get; set; } = new List<IFormFile>();
        public ICollection<CandidateGallery> ? GalleryPhotoss { get;  set; }
        public int VoteCount { get;  set; }
        public string? PositionName { get;  set; }
    }
}
