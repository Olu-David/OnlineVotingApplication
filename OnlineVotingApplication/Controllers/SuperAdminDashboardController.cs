using Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting; // Required for rate limiting attributes
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Jobs;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class SuperAdminDashboardController : Controller
    {
        private readonly HybridFormBuilderService _formBuilderService;
        private readonly AppDbContext _context;
        private readonly NotificationChannel _channel;
        private readonly ITenantProvider _tenantProvider;
        private readonly IDistributedCache _cache;
        private readonly IEmailService _emailService;
        private readonly IAuditLogService _auditLogService;
        private readonly iCandidateService _iCandidateService;

        public SuperAdminDashboardController(
            HybridFormBuilderService formBuilderService,
            AppDbContext context,
            NotificationChannel channel,
            ITenantProvider tenantProvider,
            IDistributedCache cache,
            IEmailService emailService,
            IAuditLogService auditLogService,
            iCandidateService candidateService)
        {
            _formBuilderService = formBuilderService ?? throw new ArgumentNullException(nameof(formBuilderService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _iCandidateService = candidateService ?? throw new ArgumentNullException(nameof(candidateService));
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            ViewData["Ctrl"] = "SuperAdminDashboard";
            ViewData["Action"] = "Dashboard";

            string cacheKeyActiveCount = "SuperAdmin_Dashboard_ActiveTenants_Count";
            string cacheKeyPendingCount = "SuperAdmin_Dashboard_PendingTenants_Count";

            // 1. Fetch Active/Approved Tenants Count
            int activeCount = 0;
            string? cachedActiveStr = await _cache.GetStringAsync(cacheKeyActiveCount);
            if (string.IsNullOrEmpty(cachedActiveStr))
            {
                activeCount = await _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(m => m.IsApproved == true && m.IsActive == true)
                    .CountAsync();

                await _cache.SetStringAsync(cacheKeyActiveCount, activeCount.ToString(), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            else
            {
                int.TryParse(cachedActiveStr, out activeCount);
            }

            // 2. Fetch Pending Tenants Count
            int pendingCount = 0;
            string? cachedPendingStr = await _cache.GetStringAsync(cacheKeyPendingCount);
            if (string.IsNullOrEmpty(cachedPendingStr))
            {
                pendingCount = await _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(t => !t.IsApproved && t.IsActive)
                    .CountAsync();

                await _cache.SetStringAsync(cacheKeyPendingCount, pendingCount.ToString(), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            else
            {
                int.TryParse(cachedPendingStr, out pendingCount);
            }

            // Populate the View Model
            var viewModel = new SuperAdminDashboardVM
            {
                ActiveTenantsCount = activeCount,
                PendingTenantsCount = pendingCount
            };

            return View(nameof(Dashboard), viewModel);
        }

        // --- GLOBAL FORM BUILDER ---

        [HttpGet]
        public IActionResult BuildGlobalForm()
        {
            ViewData["Ctrl"] = "SuperAdminDashboard";
            ViewData["Action"] = "BuildGlobalForm";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects global form schema modifications against rapid spam
        public async Task<IActionResult> BuildGlobalForm(string fieldName, ElectionFieldType fieldType, TenantCategory tenantCategory, string? csvChoices, bool isRequired)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                ModelState.AddModelError("", "Field label name cannot be empty.");
                return View();
            }

            await _formBuilderService.CreateGlobalCategoryFieldAsync(fieldName, fieldType, tenantCategory, csvChoices, isRequired);

            // Audit log tracking
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            await _auditLogService.LogActivityAsync(
                userId: userId ?? "",
                action: "Global Form Field Created",
                details: $"Created global template field '{fieldName}' of type '{fieldType}' for category '{tenantCategory}'",
                ipAddress: ipAddress,
                tenantId: null
            );

            TempData["SuccessMessage"] = "Global template field published successfully.";
            return RedirectToAction("BuildGlobalForm");
        }

        [HttpGet]
        public async Task<IActionResult> AllTenants(int pageNumber = 1, int pageSize = 10)
        {
            ViewData["Ctrl"] = "SuperAdminDashboard";
            ViewData["Action"] = "AllTenants";

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            string cacheKeyList = $"SuperAdmin_AllTenants_List_P{pageNumber}_S{pageSize}";
            string cacheKeyCount = "SuperAdmin_AllTenants_Count";

            // 1. Fetch or Cache Total Count
            int totalCount;
            string? cachedCountStr = await _cache.GetStringAsync(cacheKeyCount);

            if (string.IsNullOrEmpty(cachedCountStr))
            {
                // IgnoreQueryFilters() ensures SuperAdmin sees all tenants globally
                totalCount = await _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(m => m.IsApproved == true)
                    .CountAsync();

                var countOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _cache.SetStringAsync(cacheKeyCount, totalCount.ToString(), countOptions);
            }
            else
            {
                totalCount = int.TryParse(cachedCountStr, out int parsedCount) ? parsedCount : 0;
            }

            // 2. Fetch or Cache Paginated List
            List<TenantViewModel> tenantList;
            string? cachedListJson = await _cache.GetStringAsync(cacheKeyList);

            if (!string.IsNullOrEmpty(cachedListJson))
            {
                tenantList = JsonSerializer.Deserialize<List<TenantViewModel>>(cachedListJson) ?? new List<TenantViewModel>();
            }
            else
            {
                tenantList = await _context.Tenants
                    .IgnoreQueryFilters() // Bypass tenant isolation filters for SuperAdmin global listing
                    .Where(m => m.IsApproved == true)
                    .AsNoTracking()
                    .OrderByDescending(m => m.CreatedAt)
                    .Skip(skip)
                    .Take(pageSize)
                    .Select(m => new TenantViewModel
                    {
                        Id = m.Id,
                        OrganizationName = m.OrganizationName,
                        Slug = m.Slug, // Added Slug projection required by View
                        SubscriptionPlan = m.SubscriptionPlan,
                        CreatedAt = m.CreatedAt,
                        IsActive = m.IsActive,
                        TenantCategory = m.TenantCategory
                    })
                    .ToListAsync();

                string jsonToCache = JsonSerializer.Serialize(tenantList);
                var listCacheOption = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _cache.SetStringAsync(cacheKeyList, jsonToCache, listCacheOption);
            }

            // 3. Assemble Paginated Result
            var paginatedResult = new PaginatedListViewModel<TenantViewModel>
            {
                Items = tenantList,
                TotalItems = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            // 4. Wrap inside ServiceResponse to match Razor View @model expectation
            var response = new ServiceResponse<PaginatedListViewModel<TenantViewModel>>
            {
                Data = paginatedResult,
                Success = true,
                Message = "Tenants retrieved successfully."
            };

            return View(nameof(AllTenants), response);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects tenant context session switching from rapid switching abuse
        public async Task<IActionResult> SwitchContext(Guid id)
        {
            if (id == Guid.Empty) return BadRequest("Invalid Tenant ID.");

            _tenantProvider.SetTenantContext(id);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _auditLogService.LogActivityAsync(
                userId: userId ?? "",
                action: "Tenant Context Switched",
                details: $"SuperAdmin switched active context to tenant ID {id}",
                ipAddress: ipAddress,
                tenantId: id
            );

            TempData["SuccessMessage"] = "Switched tenant context successfully.";
            return RedirectToAction("Dashboard", "Tenant");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects state clearance actions against automated spam
        public async Task<IActionResult> ClearTenantContext()
        {
            _tenantProvider.ClearTenantContext();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _auditLogService.LogActivityAsync(
                userId: userId ?? "",
                action: "Tenant Context Cleared",
                details: "SuperAdmin restored default context view.",
                ipAddress: ipAddress,
                tenantId: Guid.Empty
            );

            TempData["SuccessMessage"] = "Returned to SuperAdmin context.";
            return RedirectToAction("Dashboard", "SuperAdmin");
        }

        // --- PENDING TENANT REGISTRATION APPROVALS ---
        [HttpGet]
        public async Task<IActionResult> GetAllPendingTenants(int pageNumber = 1, int pageSize = 20)
        {
            ViewData["Ctrl"] = "SuperAdminDashboard";
            ViewData["Action"] = "GetAllPendingTenants";

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            string cacheCountKey = "PendingTenant_TotalCount";
            string cacheListKey = $"PendingTenant_List_P{pageNumber}_S{pageSize}";

            string? cachedCountStr = await _cache.GetStringAsync(cacheCountKey);
            int totalCount;

            if (string.IsNullOrEmpty(cachedCountStr))
            {
                totalCount = await _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(t => !t.IsApproved && t.IsActive)
                    .CountAsync();

                var countOption = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _cache.SetStringAsync(cacheCountKey, totalCount.ToString(), countOption);
            }
            else
            {
                totalCount = int.TryParse(cachedCountStr, out int parsedCount) ? parsedCount : 0;
            }

            string? cachedListJson = await _cache.GetStringAsync(cacheListKey);
            List<Tenant> pendingTenants;

            if (!string.IsNullOrEmpty(cachedListJson))
            {
                pendingTenants = JsonSerializer.Deserialize<List<Tenant>>(cachedListJson) ?? new List<Tenant>();
            }
            else
            {
                pendingTenants = await _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(t => !t.IsApproved && t.IsActive)
                    .OrderByDescending(t => t.CreatedAt)
                    .Skip(skip)
                    .Take(pageSize)
                    .AsNoTracking()
                    .ToListAsync();

                var listOption = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _cache.SetStringAsync(cacheListKey, JsonSerializer.Serialize(pendingTenants), listOption);
            }

            var paginatedResult = new PaginatedListViewModel<Tenant>
            {
                Items = pendingTenants,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalCount
            };

            return View(nameof(GetAllPendingTenants), paginatedResult);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects tenant approval actions from bulk automation/spam
        public async Task<IActionResult> AcceptTenant(Guid id)
        {
            if (id == Guid.Empty) return BadRequest();

            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound();

            tenant.IsApproved = true;
            await _context.SaveChangesAsync();

            var adminUser = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == id);

            if (adminUser != null)
            {
                adminUser.IsApproved = true;

                string subject = "Tenant Account Approved";
                string message = $@"<p>Hello {adminUser.UserName},</p>
                                    <p>Your organization <strong>{tenant.OrganizationName}</strong> has been successfully approved.</p>";

                await _emailService.EmailSendAsync(adminUser.Email ?? "Unknown", subject, message);

                await _context.SaveChangesAsync();
            }

            await _cache.RemoveAsync("PendingTenant_TotalCount");

            // Audit log tracking
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            await _auditLogService.LogActivityAsync(
                userId: userId ?? "",
                action: "Tenant Approved",
                details: $"Approved organization '{tenant.OrganizationName}'",
                ipAddress: ipAddress,
                tenantId: id != Guid.Empty ? id : null
            );

            TempData["SuccessMessage"] = $"Organization '{tenant.OrganizationName}' has been successfully approved.";
            return RedirectToAction(nameof(GetAllPendingTenants));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects tenant rejection actions from rapid spam clicks
        public async Task<IActionResult> RejectTenant(Guid id)
        {
            if (id == Guid.Empty) return BadRequest();

            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound();

            tenant.IsActive = false;
            await _context.SaveChangesAsync();

            var adminUser = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == id);

            if (adminUser != null)
            {
                string subject = "Tenant Account Registration Rejected";
                string message = $@"<p>Hello {adminUser.UserName},</p>
                                    <p>We regret to inform you that your organization registration for <strong>{tenant.OrganizationName}</strong> has been rejected.</p>";

                await _emailService.EmailSendAsync(adminUser.Email ?? "Unknown", subject, message);
            }

            await _cache.RemoveAsync("PendingTenant_TotalCount");

            // Audit log tracking
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            await _auditLogService.LogActivityAsync(
                userId: userId ?? "",
                action: "Tenant Rejected",
                details: $"Rejected organization registration for '{tenant.OrganizationName}'",
                ipAddress: ipAddress,
                tenantId: id != Guid.Empty ? id : null
            );

            TempData["ErrorMessage"] = $"Organization '{tenant.OrganizationName}' registration was rejected.";
            return RedirectToAction(nameof(GetAllPendingTenants));
        }

        [HttpGet]
        public async Task<IActionResult> CreateCandidateSuperAdmin()
        {
            ViewBag.Tenants = await _context.Tenants
                .AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.IsActive)
                .OrderBy(t => t.OrganizationName)
                .ToListAsync();

            return View(new SuperAdminCandidateCreationViewModel());
        }

        // JSON Cascade Endpoint called via JavaScript on Tenant change
        [HttpGet]
        public async Task<IActionResult> GetElectionsByTenant(Guid tenantId)
        {
            if (tenantId == Guid.Empty)
            {
                return Json(new List<object>());
            }

            var elections = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e => e.TenantId == tenantId && !e.IsDeleted)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new
                {
                    id = e.Id,
                    title = e.Title
                })
                .ToListAsync();

            return Json(elections);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects global candidate creation from script injections and form spam
        public async Task<IActionResult> CreateCandidateSuperAdmin(SuperAdminCandidateCreationViewModel model)
        {
            // 1. Resolve User ID directly as string or Guid without premature returns
            var superAdminUserClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(superAdminUserClaim, out Guid superAdminUserId);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            // 2. Validate ModelState
            if (!ModelState.IsValid)
            {
                ViewBag.Tenants = await _context.Tenants
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(t => t.IsActive && t.IsApproved)
                    .OrderBy(t => t.OrganizationName)
                    .ToListAsync();

                return View(model);
            }

            // 3. Process candidate creation via service
            var result = await _iCandidateService.CreateCandidateBySuperAdminAsync(model);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? string.Empty);

                ViewBag.Tenants = await _context.Tenants
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(t => t.IsActive && t.IsApproved)
                    .OrderBy(t => t.OrganizationName)
                    .ToListAsync();

                return View(model);
            }

            // 4. Audit Logging (superAdminUserId is string or Guid depending on your service overload)
            await _auditLogService.LogActivityAsync(
                userId: superAdminUserClaim ?? superAdminUserId.ToString(),
                action: "SuperAdmin Candidate Creation",
                details: $"SuperAdmin added candidate '{model.CandidateEmail}' under Tenant ID: {model.TenantId}",
                ipAddress: ipAddress,
                tenantId: model.TenantId
            );

            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction("AllCandidates");
        }
    }
}