using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Jobs
{
    public record VoteJob
   (

        string VoterId,
        Guid ElectionId,
        Guid CandidateId,
        Guid? PositionId,
        Guid? StateId,
        Guid? TenantId,
        string ConfirmationCode,
        CancellationToken CancellationToken = default);
    }


   

 