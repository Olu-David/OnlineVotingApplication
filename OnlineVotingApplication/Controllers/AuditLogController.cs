
    using global::OnlineVotingApplication.Repository.iServices;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using OnlineVotingApplication.Services;
    using System;
    using System.Threading.Tasks;

    namespace OnlineVotingApplication.Controllers
    {
        [Authorize(Roles = "SuperAdmin,Official")] // Restricts view access to administrators and election officials only
        public class AuditLogController : Controller
        {
            private readonly IAuditLogService _auditLogService;

            public AuditLogController(IAuditLogService auditLogService)
            {
                _auditLogService = auditLogService;
            }

            // GET: /AuditLog/Index
            [HttpGet]
            public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 50, string? tenantIdStr = null)
            {
                Guid? tenantId = null;
                if (!string.IsNullOrEmpty(tenantIdStr) && Guid.TryParse(tenantIdStr, out var parsedGuid))
                {
                    tenantId = parsedGuid;
                }

                // Fetch the paginated logs through the service
                var logs = await _auditLogService.GetLogsAsync(pageNumber, pageSize, tenantId);

                // Pass pagination and filter states to the view via ViewBag
                ViewBag.CurrentPage = pageNumber;
                ViewBag.PageSize = pageSize;
                ViewBag.SelectedTenantId = tenantIdStr;

                return View(logs);
            }
        }
    }

