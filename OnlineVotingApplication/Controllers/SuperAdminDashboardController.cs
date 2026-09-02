using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public SuperAdminDashboardController(
            HybridFormBuilderService formBuilderService,
            AppDbContext context,
            NotificationChannel channel,
            ITenantProvider tenantProvider,
            IDistributedCache cache, IEmailService emailService)
        {
            _formBuilderService = formBuilderService ?? throw new ArgumentNullException(nameof(formBuilderService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _emailService = emailService;
        }
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
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

            return View("~/Views/SuperAdminDashboard/Dashboard.cshtml", viewModel);
        }
        // --- GLOBAL FORM BUILDER ---

        [HttpGet]
        public IActionResult BuildGlobalForm()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BuildGlobalForm(string fieldName, ElectionFieldType fieldType, TenantCategory tenantCategory, string? csvChoices, bool isRequired)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                ModelState.AddModelError("", "Field label name cannot be empty.");
                return View();
            }

            await _formBuilderService.CreateGlobalCategoryFieldAsync(fieldName, fieldType, tenantCategory, csvChoices, isRequired);

            TempData["SuccessMessage"] = "Global template field published successfully.";
            return RedirectToAction("BuildGlobalForm");
        }

        // --- GLOBAL TENANT LISTING & CONTEXT SWITCHING ---

        [HttpGet]
        public async Task<IActionResult> AllTenants(int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            string cacheKeyList = $"SuperAdmin_AllTenants_List_P{pageNumber}_S{pageSize}";
            string cacheKeyCount = "SuperAdmin_AllTenants_Count";

            int totalCount;
            string? cachedCountStr = await _cache.GetStringAsync(cacheKeyCount);

            if (string.IsNullOrEmpty(cachedCountStr))
            {
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

            List<TenantViewModel> tenantList;
            string? cachedListJson = await _cache.GetStringAsync(cacheKeyList);

            if (!string.IsNullOrEmpty(cachedListJson))
            {
                tenantList = JsonSerializer.Deserialize<List<TenantViewModel>>(cachedListJson) ?? new List<TenantViewModel>();
            }
            else
            {
                tenantList = await _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(m => m.IsApproved == true)
                    .AsNoTracking()
                    .OrderByDescending(m => m.CreatedAt)
                    .Skip(skip)
                    .Take(pageSize)
                    .Select(m => new TenantViewModel
                    {
                        OrganizationName = m.OrganizationName,
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

            var paginatedResult = new PaginatedListViewModel<TenantViewModel>
            {
                Items = tenantList,
                TotalItems = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            return View("~/Views/SuperAdminDashboard/AllTenants.cshtml", paginatedResult);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SwitchContext(Guid id)
        {
            if (id == Guid.Empty) return BadRequest("Invalid Tenant ID.");

            _tenantProvider.SetTenantContext(id);

            TempData["SuccessMessage"] = "Switched tenant context successfully.";
            return RedirectToAction("Dashboard", "Tenant");
        }

        // --- PENDING TENANT REGISTRATION APPROVALS ---

        [HttpGet]
        public async Task<IActionResult> GetAllPendingTenants(int pageNumber = 1, int pageSize = 20)
        {
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

            return View("~/Views/SuperAdminDashboard/PendingTenants.cshtml", paginatedResult);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptTenant(Guid id)
        {
            if (id == Guid.Empty) return BadRequest();

            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound();

            tenant.IsApproved = true;
            await _context.SaveChangesAsync();

            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.TenantId == id);
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

            TempData["SuccessMessage"] = $"Organization '{tenant.OrganizationName}' has been successfully approved.";
            return RedirectToAction(nameof(GetAllPendingTenants));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectTenant(Guid id)
        {
            if (id == Guid.Empty) return BadRequest();

            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound();

            tenant.IsActive = false;
            await _context.SaveChangesAsync();

            // Fetch the admin user associated with this tenant
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.TenantId == id);
            if (adminUser != null)
            {
                string subject = "Tenant Account Registration Rejected";
                string message = $@"<p>Hello {adminUser.UserName},</p>
                            <p>We regret to inform you that your organization registration for <strong>{tenant.OrganizationName}</strong> has been rejected.</p>";

                await _emailService.EmailSendAsync(adminUser.Email ?? "Unknown", subject, message);
            }

            await _cache.RemoveAsync("PendingTenant_TotalCount");

            TempData["ErrorMessage"] = $"Organization '{tenant.OrganizationName}' registration was rejected.";
            return RedirectToAction(nameof(GetAllPendingTenants));
        }
    }
}