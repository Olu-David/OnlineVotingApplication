using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface IElectionService
    {
        Task<ServiceResponse<ElectionDto>> CreateElectionAsync(ElectionDto model, string UserId);
        Task<ServiceResponse<bool>> EndElectionAsync(Guid electionId, string UserId);

        Task<ServiceResponse<bool>> StartElectionAsync(Guid electionId, string UserId);

    }
    
}
