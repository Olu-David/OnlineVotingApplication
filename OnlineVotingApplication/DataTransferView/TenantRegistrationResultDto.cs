using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.DataTransferView
{
    public class TenantRegistrationResultDto
    {
        public Tenant Tenant { get; set; } = default!;
        public ApplicationUser AdminUser { get; set; } = default!;
        public string UserId { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
    }
}