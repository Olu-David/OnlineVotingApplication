using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin,Official")]
    public class AuditLogController : Controller
    {
        private readonly IAuditLogService _auditLogService;

        public AuditLogController(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        // GET: /AuditLog/Index
        [HttpGet]
        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 50)
        {
            Guid? tenantIdFilter = null;

            // Check if the current user is a SuperAdmin. If NOT, restrict them strictly to their own Tenant ID.
            if (!User.IsInRole("SuperAdmin"))
            {
                // Extract the TenantId from the logged-in user's claims
                var tenantClaim = User.FindFirst("TenantId")?.Value;

                if (Guid.TryParse(tenantClaim, out var parsedTenantId))
                {
                    tenantIdFilter = parsedTenantId;
                }
                else
                {
                    // Fallback if an official somehow lacks a valid tenant claim
                    return Forbid();
                }
            }
            // If they ARE a SuperAdmin, tenantIdFilter stays null, meaning GetLogsAsync pulls every record platform-wide!

            var logs = await _auditLogService.GetLogsAsync(pageNumber, pageSize, tenantIdFilter);

            ViewBag.CurrentPage = pageNumber;
            ViewBag.PageSize = pageSize;

            return View(logs);
        }
    }
}