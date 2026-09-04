using Google;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Hubs;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Services
{
    public class SupportService : ISupportService
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<DashboardHub> _hubContext;

        public SupportService(AppDbContext context, IHubContext<DashboardHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task CreateTicketAsync(string name, string email, string subject, string message, string ipAddress, Guid? tenantId = null)
        {
            var ticket = new SupportTicket
            {
                Name = name,
                Email = email,
                Subject = subject,
                Message = message,
                IpAdidress = ipAddress,
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow,
                IsResolved = false
            };

            _context.SupportTickets.Add(ticket);
            await _context.SaveChangesAsync();

            // Broadcast live to the admin support dashboard via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveSupportTicket", new
            {
                id = ticket.Id,
                name = ticket.Name,
                email = ticket.Email,
                subject = ticket.Subject,
                message = ticket.Message,
                ipAddress = ticket.IpAdidress,
                createdAt = ticket.CreatedAt.ToString("g"),
                tenantId = ticket.TenantId.HasValue ? ticket.TenantId.ToString() : "Global"
            });
        }

        public async Task<IEnumerable<SupportTicket>> GetAllTicketsAsync(Guid? tenantId = null)
        {
            var query = _context.SupportTickets.AsNoTracking().AsQueryable();

            if (tenantId.HasValue && tenantId != Guid.Empty)
            {
                query = query.Where(t => t.TenantId == tenantId.Value);
            }

            return await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        }

        public async Task<PaginatedListViewModel<SupportTicket>> GetPaginatedTicketsAsync( int pageNumber = 1,int pageSize = 10,Guid? tenantId = null)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            var query = _context.SupportTickets
                .AsNoTracking()
                .AsQueryable();

            // Filter by tenant if explicit tenant ID is supplied
            if (tenantId.HasValue && tenantId != Guid.Empty)
            {
                query = query.Where(t => t.TenantId == tenantId.Value);
            }
            else
            {
                // Bypass global EF core query filters if SuperAdmin needs to see all tenant tickets
                query = query.IgnoreQueryFilters();
            }

            // Get total items count before skipping/taking
            int totalItems = await query.CountAsync();

            // Get paginated slice
            var items = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            return new PaginatedListViewModel<SupportTicket>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };
        }

        public async Task ResolveTicketAsync(int ticketId)
        {
            var ticket = await _context.SupportTickets.FindAsync(ticketId);
            if (ticket != null)
            {
                ticket.IsResolved = true;
                await _context.SaveChangesAsync();
            }
        }
    }
}