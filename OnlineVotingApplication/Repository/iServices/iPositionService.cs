using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{ 
    public interface iPositionService
    {
        Task<ServiceResponse<string>> UpdatePosition(EditPositionModel model, string ID);
        Task<PaginatedListViewModel<PositionDTO>> AllSoftDeleted(int PageNumber = 1, int PageSize = 10);
        Task<PaginatedListViewModel<PositionDTO>> GetAllPositionsAsync(string id, int pageNumber = 1, int pageSize = 10);
        Task<bool> GetPositionByIdAsync(Guid id);
        Task<ServiceResponse<string>> DeletePosition(Guid ID, string userId, CancellationToken cancellationToken = default);
        Task<ServiceResponse<string>> CreatePositionAsync(PositionDTO model, string name);
    }
}
