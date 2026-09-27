using Microsoft.AspNetCore.Http;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.DataTransferView
{
    public class ElectionViewModel
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; } // 👈 Added
        public int ElectionYear { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public TenantCategory Category { get; set; }
        public Guid? TenantId { get; set; }
        public string? TenantName { get; set; }
        public DateTime? DeletedAt { get; set; }

        public IFormFile? UrlImage { get; set; } // 👈 Added (for incoming file uploads)
        public string? PhotoImage { get; set; } // 👈 Added (for displaying the image URL)
        public string? RegistrationLink { get; set; } // 👈 Recommended based on your service code
    }
}