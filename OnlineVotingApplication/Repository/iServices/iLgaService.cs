using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iLgaService
    {
        Task<ServiceResponse<string>> CreateLgaAsync(LgaDTO lga, string Id);
        Task<PaginatedListViewModel<LgaDTO>> GetAllLgasAsync(int PageNumber = 1, int PageSize = 10);
        Task<PaginatedListViewModel<LgaDTO>> GetLgasByStateIdAsync(Guid stateId, int PageNumber = 1, int PageSize = 10);
        Task<LGA?> GetLgaByIdAsync(Guid id);
        Task<ServiceResponse<string>> DeleteLgaAsync(LgaDTO dto, string ID);
    }
}
