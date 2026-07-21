namespace OnlineVotingApplication.DataTransferView
{
    public class ResetPasswordViewModel
    {
        public string Token { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}