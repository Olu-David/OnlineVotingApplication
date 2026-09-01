using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface IElectionService
    {
        Task<ServiceResponse<ElectionDto>> CreateElectionAsync(ElectionDto model, string UserId);
        Task<ServiceResponse<bool>> EndElectionAsync(Guid electionId, string UserId);
        Task<ServiceResponse<List<ElectionEvent>>> GetPastElectionsAsync(Guid? tenantId = null);
        Task<ServiceResponse<bool>> StartElectionAsync(Guid electionId, string UserId);
        Task<ServiceResponse<PaginatedListViewModel<ElectionEvent>>> GetPagedElectionsAsync(int pageNumber, int pageSize, Guid? tenantId = null);

        }
    }

    

