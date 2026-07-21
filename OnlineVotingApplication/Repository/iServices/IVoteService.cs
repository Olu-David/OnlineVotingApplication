using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface IVoteService
    {
        Task<ServiceResponse<string>> CastVoteAsync(string voterId, Guid candidateId, Guid electionId, Guid positionId, string UserId, string Id);
        Task<ServiceResponse<List<VoteResultDto>>> GetResultsAsync(Guid electionId, Guid positionId);
        Task<ServiceResponse<List<StateResult_Dto>>> GetVoteByStateViaPosition(Guid electionID, Guid positionID);
        }
    
}
