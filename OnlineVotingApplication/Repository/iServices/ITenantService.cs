using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface ITenantService
    {
        Task<ServiceResponse<TenantRegistrationResultDto>> RegisterTenantOrganizationAsync(TenantRegistrationViewModel model);
        Task<Tenant?> GetTenantDetailsAsync();
        Task<ServiceResponse<PaginatedListViewModel<TenantViewModel>>> AllTenantListAsync(string Id, int pageNumber = 1, int pageSize = 10);

        Task<TenantMetricsDto> GetDashboardMetricsAsync();
        Task<bool> IsWithinPlanLimitsAsync();
        Task<bool> UpdateSubscriptionPlanAsync(string newPlan, int newElectionLimit);
        Task<bool> ToggleTenantStatusAsync(Guid tenantId, bool isActive);
        Task<List<UserListSummaryDto>> GetTenantAdminsAsync();
        Task<List<Candidate>> GetElectionCandidatesAsync(Guid electionId);
        Task<int> GetElectionVoterCountAsync(Guid electionId);

        // 🌟 NEW SAAS SYSTEM LIFECYCLE CAPABILITIES
      
        Task<bool> IsElectionOwnedByActiveTenantAsync(Guid electionId);
    }
}

