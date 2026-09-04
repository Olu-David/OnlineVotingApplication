using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface ISupportService
    {
        Task CreateTicketAsync(string name, string email, string subject, string message, string ipAddress, Guid? tenantId = null);
        Task<IEnumerable<SupportTicket>> GetAllTicketsAsync(Guid? tenantId = null);
        Task ResolveTicketAsync(int ticketId);
        Task<PaginatedListViewModel<SupportTicket>> GetPaginatedTicketsAsync(int pageNumber = 1, int pageSize = 10, Guid? tenantId = null);
        
        }
}
