using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iLgaService
    {
        Task<ServiceResponse<string>> CreateLgaAsync(LgaDTO lga, string Id);
        Task<List<LgaDTO>> GetAllLgasAsync();
        Task<List<LgaDTO>> GetLgasByStateIdAsync(Guid stateId);
        Task<LGA?> GetLgaByIdAsync(Guid id);
        Task<ServiceResponse<string>> DeleteLgaAsync(LgaDTO dto);
    }
}
