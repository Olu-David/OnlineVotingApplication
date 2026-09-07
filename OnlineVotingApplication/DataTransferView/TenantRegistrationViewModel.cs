using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.DataTransferView
{
    public class TenantRegistrationViewModel
    {
        [Required(ErrorMessage = "Organization name is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "The organization name must be between 2 and 100 characters.")]
        [Display(Name = "Organization Name")]
        public string OrganizationName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a tenant category.")]
        [Display(Name = "Category")]
        public TenantCategory TenantCategory { get; set; }

        [Required(ErrorMessage = "Admin email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Admin Email")]
        public string AdminEmail { get; set; } = string.Empty;

        [DataType(DataType.Upload)]
        [Display(Name = "Organization Logo")]
        public IFormFile? ProfilePicture { get; set; }

        [Required(ErrorMessage = "Admin password is required.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "The password must be at least 6 characters long.")]
        [Display(Name = "Password")]
        public string AdminPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirming your password is required.")]
        [DataType(DataType.Password)]
        [Compare("AdminPassword", ErrorMessage = "The password and confirmation password do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmAdminPassword { get; set; } = string.Empty;

        [Url(ErrorMessage = "Please enter a valid URL.")]
        public string BaseUrl { get; set; } = string.Empty;
    }
}