using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface IPartyService
    {
        Task<ServiceResponse<string>> CreatePartyAsync(PartyViewModel model, string Id);
        Task<PaginatedListViewModel<PartyViewModel>> AllPartyAsync(int PageNumber = 1, int PageSize = 10);
        Task<ServiceResponse<String>> EditPartyAsync(EditPartyViewModel model, string Id);
        Task <ServiceResponse< bool>> SoftDeletePartyAsync( string Id, Guid PartyID);
    }
}
