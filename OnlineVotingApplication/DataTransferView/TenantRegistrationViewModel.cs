using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.DataTransferView
{
    public class TenantRegistrationViewModel
    {
        public string OrganizationName { get; set; } = string.Empty;
        public TenantCategory TenantCategory { get;  set; }
        public string? AdminEmail { get;  set; }
        public IFormFile ProfilePicture { get; set; } = null!;
        public string? AdminPassword { get;  set; }
        public string BaseUrl { get;  set; }= string.Empty; 
    }
}