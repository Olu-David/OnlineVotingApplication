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