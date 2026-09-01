using Google;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Hubs;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<DashboardHub> _hubContext; // <--- 1. Inject Hub Context

        public AuditLogService(AppDbContext context, IHubContext<DashboardHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task LogActivityAsync(string userId, string action, string details, string ipAddress, Guid? tenantId = null)
        {
            var log = new AuditLog
            {
                UserId = userId,
                Action = action,
                Details = details,
                IpAddress = ipAddress,
                TenantId = tenantId,
                Timestamp = DateTime.UtcNow
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            // <--- 2. Broadcast the new log live via WebSockets!
            await _hubContext.Clients.All.SendAsync("ReceiveAuditLog", new
            {
                timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                userId = log.UserId ?? "System/Guest",
                action = log.Action,
                details = log.Details,
                ipAddress = log.IpAddress,
                tenantId = log.TenantId.HasValue ? log.TenantId.ToString() : "Global"
            });
        }

        public async Task<IEnumerable<AuditLog>> GetLogsAsync(int pageNumber = 1, int pageSize = 50, Guid? tenantId = null)
        {
            var query = _context.AuditLogs.AsNoTracking().AsQueryable();

            if (tenantId.HasValue && tenantId != Guid.Empty)
            {
                query = query.Where(l => l.TenantId
                == tenantId.Value);
            }

            return await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
    }
}