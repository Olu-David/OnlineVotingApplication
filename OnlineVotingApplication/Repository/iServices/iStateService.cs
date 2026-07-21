using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iStateService
    {
        Task<bool> CreateStateAsync(StateDTO state);
        Task<States?> GetStateByIdAsync(Guid id);
        Task<List<StateDTO>> GetAllStatesAsync();
        Task<bool> DeleteStateAsync(Guid id);
    }
}
