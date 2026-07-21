using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iPositionService
    {
        Task<ServiceResponse<string>> UpdatePosition(PositonDTO model, string ID);
        Task<List<PositonDTO>> GetAllPositionsAsync();
        Task<bool> GetPositionByIdAsync(Guid id);
        Task<ServiceResponse<string>> DeletePosition(Guid ID, string userId, CancellationToken cancellationToken = default);
        Task<ServiceResponse<string>> CreatePositionAsync(PositonDTO model, string name);
    }
}
