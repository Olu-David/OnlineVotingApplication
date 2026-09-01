using OnlineVotingApplication.Models;
using System;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface IAuditLogService
    {
     
        Task LogActivityAsync(string userId, string action, string details, string ipAddress, Guid? tenantId = null);

        // Retrieves paginated audit logs, with an optional tenant filter for multi-tenant isolation
        Task<IEnumerable<AuditLog>> GetLogsAsync(int pageNumber = 1, int pageSize = 50, Guid? tenantId = null);
    }
}
