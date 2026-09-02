using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.DataTransferView
{
    public class TenantRegistrationViewModel
    {
        [Required(ErrorMessage = "Organization name is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "The organization name must be between 2 and 100 characters.")]
        public string OrganizationName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a tenant category.")]
        public TenantCategory TenantCategory { get; set; }

        [Required(ErrorMessage = "Admin email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string? AdminEmail { get; set; }

        // Made nullable since your service layer handles default image fallback if null
        [DataType(DataType.Upload)]
        public IFormFile? ProfilePicture { get; set; }

        [Required(ErrorMessage = "Admin password is required.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "The password must be at least 6 characters long.")]
        public string? AdminPassword { get; set; }

        [Url(ErrorMessage = "Please enter a valid URL.")]
        public string BaseUrl { get; set; } = string.Empty;
    }
}