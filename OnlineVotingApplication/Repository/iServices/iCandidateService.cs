using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iCandidateService
    {
        Task<PaginatedListViewModel<CandidateViewModel>> GetAllCandidates(int PageNumber = 1, int PageSize = 10);
        Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetAllSoftDeletedCandidate(string UserId, int pageNumber = 1, int pageSize = 10);
        Task<ServiceResponse<string>> CreateCandidateAsync(CandidateViewModel model, string userId, string token);
        Task<ServiceResponse<string>> UpdateCandidateAsync(UpdateCandidateViewModel model, string Id, CancellationToken token);
        Task<ServiceResponse<string>> SendCandidateInviteAsync(SendCandidateInvitation model);
        Task<ServiceResponse<bool>> SoftDeleteCandidateAsync(Guid candidateId, string userId, CancellationToken cancellationToken = default);
        Task<ServiceResponse<bool>> RestoreCandidateDeleteAsync(Guid Id, string UserId);
     
        Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByPositionAsync(Guid positionId, int pageNumber = 1, int pageSize = 10);
        Task<ServiceResponse<CandidateViewModel>> GetCandidateByIdAsync(Guid id);
        Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByLgaAsync(Guid? LgaId, int pageNumber = 1, int pageSize = 10);
        Task<PaginatedListViewModel<CandidateViewModel>> GetAllCandidateViaParty(Guid partyId, int pageNumber = 1, int pageSize = 10);
        Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByStateAsync(Guid? stateId, int pageNumber = 1, int pageSize = 10);
        void ClearCandidateCache(int pageNumber, int pageSize);
    }
}
