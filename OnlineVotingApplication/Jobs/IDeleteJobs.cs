using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Jobs
{
    public interface IDeleteJobs;
    public record DeleteCandidate (Guid CandidateID, string Id, DeleteType type):IDeleteJobs;
    public record DeleteLga(LgaDTO model, DeleteType type) :IDeleteJobs;
    public record DeletePosition(Guid ID, string Id, DeleteType type) :IDeleteJobs;
    

    
    
}
