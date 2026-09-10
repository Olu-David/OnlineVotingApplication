using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{ 
    public interface iPositionService
    {
        Task<PaginatedListViewModel<PositionDTO>> GetAllPositionsAsync(Guid electionId, int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default);
        Task<bool> GetPositionByIdAsync(Guid id);
        Task<ServiceResponse<string>> CreatePositionAsync(PositionDTO model, string userId, Guid electionId);
        Task<ServiceResponse<string>> DeletePosition(Guid ID, string userId, CancellationToken cancellationToken = default);
        Task<ServiceResponse<string>> UpdatePosition(EditPositionModel model, string userId);
        Task<PaginatedListViewModel<PositionDTO>> AllSoftDeleted(int PageNumber = 1, int PageSize = 10);
    }
}
