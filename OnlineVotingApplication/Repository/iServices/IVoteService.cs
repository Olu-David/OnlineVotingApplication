using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface IVoteService
    {
        Task<ServiceResponse<List<VoteResultDto>>> GetResultsAsync(Guid electionId, Guid positionId);
        Task<ServiceResponse<List<StateResultDto>>> GetVoteByStateViaPosition(Guid electionID, Guid positionID);
        Task<ServiceResponse<string>> GenerateAndQueueConfirmationCodeAsync(string voterId, Guid electionId);
        Task<ServiceResponse<string>> ConfirmAndCastVoteAsync(string voterId, Guid electionId, string enteredCode, Guid candidateId, Guid positionId);
        Task<ServiceResponse<List<ElectionEvent>>> GetElectionsTakenByVoterAsync(string voterId);
        Task<ServiceResponse<List<Vote>>> GetVoterBallotHistoryAsync(string voterId, Guid electionId);
        Task<ServiceResponse<string>> PenalizeVoterAsync(string voterId, Guid electionId, string reason, string adminId);

        // Method signature with searchTerm first and pagination parameters
        Task<ServiceResponse<PaginatedListViewModel<VoterDto>>> GetAllVotersAsync(string? searchTerm = null, int pageNumber = 1, int pageSize = 10);

        Task<ServiceResponse<PaginatedListViewModel<PenalizedVoterDto>>> GetPenalizedVotersAsync(int pageNumber, int pageSize);
        Task<ServiceResponse<List<CandidateVoteDto>>> GetElectionResultsAsync(Guid electionEventId);
        Task<ServiceResponse<string>> BulkPenalizeVotersAsync(List<string> voterIds, Guid electionId, string reason, string adminId);
        Task<ServiceResponse<PaginatedListViewModel<ElectionDto>>> GetElectionsForVoterAsync(
            string? searchTerm,
            string? sortBy,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);
    }
}