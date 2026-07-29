using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iStateService
    {
        Task<bool> CreateStateAsync(StateDTO state, string Id);
        Task<bool> UpdateStateAsync(UpdateStateDto model, string Id);
        Task<States?> GetStateByIdAsync(Guid id);
        Task<List<StateDTO>> GetAllStatesAsync();
        Task<bool> DeleteStateAsync(Guid id);
    }
}
