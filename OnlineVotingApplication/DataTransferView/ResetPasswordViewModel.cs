using System.ComponentModel.DataAnnotations;

namespace OnlineVotingApplication.DataTransferView
{
    public class ResetPasswordViewModel
    {
        public string Token { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}