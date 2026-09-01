using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    public class LoginViewModel
    {
        [EmailAddress]
        [Required(ErrorMessage = "Enter your EmailAddress ")]
        public string EmailAddress { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = null!;
        public bool RememberMe { get;  set; }
        public string? Email { get; internal set; }
    }
}
