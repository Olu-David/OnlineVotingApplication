using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    public class RegistrationViewModel
    {
        [Required(ErrorMessage = "Enter your FirstName")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "First name must be at least 2 characters long")]
        public string? FirstName { get; set; }

        [Required(ErrorMessage = "Enter your LastName")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Last name must be at least 2 characters long")]
        public string? LastName { get; set; } = null;

        [EmailAddress]
        [Required(ErrorMessage = "Enter your EmailAddress")]
        public string? EmailAddress { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long")]
        public string Password { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }

        [Phone]
        [Required(ErrorMessage = "Enter Your PhoneNumber")]
        public string? PhoneNumber { get; set; } // Changed from internal to public for model binder access


    }
}
 