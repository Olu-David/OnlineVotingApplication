namespace OnlineVotingApplication.DataTransferView
{
    public class LoginWith2faViewModel
    {
        public string? UserId { get; set; }
        public bool RememberMe { get; set; }
        public string? TwoFactorCode { get; internal set; }
    }
}