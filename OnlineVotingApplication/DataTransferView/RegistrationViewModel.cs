using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    public class RegistrationViewModel
    {
        [Required(ErrorMessage ="Enter your FirstName")]
        [StringLength(100, MinimumLength =6, ErrorMessage ="Password must be at least 6 characters long")]
        public string? FirstName { get; set; }
        [Required(ErrorMessage = "Enter your LastName")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long")]
        public string? LastName { get; set; } = null;
        [EmailAddress]
        [Required(ErrorMessage ="Enter your EmailAddress ")]
         public string? EmailAddress { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name ="Confirm Password")]
        [Compare("Password")]
        public string? ConfirmPassword { get; set; }
        [Phone]
        [Required(ErrorMessage ="Enter Your PhoneNumber")]
        public string? PhoneNumber { get; internal set; }
        [Required]
        public string Roles { get; internal set; } = null!;
        public DateTime DateOfBirth { get; set; }
    }

}
