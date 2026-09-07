using Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    [Authorize(Roles = "SuperAdmin, PlatformAdmin")]
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
        private readonly UserManager<ApplicationUser> _userManager;
        private const string UserStatsCacheKey = "super_admin_user_stats_cache";

        #region SuperAdminDashboardController
        public SuperAdminDashboardController(HybridFormBuilderService formBuilderService, AppDbContext context, NotificationChannel channel, ITenantProvider tenantProvider, IDistributedCache cache, IEmailService emailService, IAuditLogService auditLogService, iCandidateService iCandidateService, UserManager<ApplicationUser> userManager)
        {
            _formBuilderService = formBuilderService ?? throw new ArgumentNullException(nameof(formBuilderService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _iCandidateService = iCandidateService ?? throw new ArgumentNullException(nameof(iCandidateService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }
        #endregion

        #region Dashboard
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
        #endregion

        #region BuildGlobalForm
        // --- GLOBAL FORM BUILDER ---

        [HttpGet]
        public IActionResult BuildGlobalForm()
        {
            ViewData["Ctrl"] = "SuperAdminDashboard";
            ViewData["Action"] = "BuildGlobalForm";
            return View();
        }
        #endregion

        #region BuildGlobalForm (2)
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
        #endregion

        #region AllTenants
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
        #endregion

        #region SwitchContext
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
        #endregion

        #region ClearTenantContext
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
        #endregion

        #region GetAllPendingTenants
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
        #endregion

        #region AcceptTenant
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
        #endregion

        #region RejectTenant
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
        #endregion
        #region CreateCandidateSuperAdmin
        // ==========================================
        // GET: /super-admin/candidates/create
        // ==========================================
        [HttpGet("create")]
        public async Task<IActionResult> CreateCandidateSuperAdmin()
        {
            await PopulateDropdownsAsync();
            return View(new SuperAdminCandidateCreationViewModel());
        }
        #endregion

        #region CreateCandidateSuperAdmin (2)
        // ==========================================
        // POST: /super-admin/candidates/create
        // ==========================================
        [HttpPost("create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCandidateSuperAdmin(SuperAdminCandidateCreationViewModel model)
        {
            // 1. Validate form input model state
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please correct the errors on the form.";
                await PopulateDropdownsAsync(model.TenantId);
                return View(model);
            }

            // 2. Call your service method
            var serviceResponse = await _iCandidateService.CreateCandidateBySuperAdminAsync(model);

            if (!serviceResponse.Success)
            {
                // Re-populate dropdowns so the form doesn't break when redisplayed
                await PopulateDropdownsAsync(model.TenantId);
                ModelState.AddModelError(string.Empty, serviceResponse.Message ?? "");
                TempData["Error"] = serviceResponse.Message;
                return View(model);
            }

            // 3. Success handling
            TempData["Success"] = serviceResponse.Message;
            return RedirectToAction("Candidate", "AllCandidate", new { model.CandidateEmail });
        }
        #endregion


        #region PopulateDropdownsAsync
        // ==========================================
        // HELPER: Populates dropdowns safely to prevent null refs
        // ==========================================
        private async Task PopulateDropdownsAsync(Guid? selectedTenantId = null)
        {
            var tenants = await _context.Tenants
                .Where(t => !t.IsActive)
                .OrderBy(t => t.OrganizationName)
                .ToListAsync();

            ViewBag.Tenants = new SelectList(tenants, "Id", "Name", selectedTenantId);

            var electionsQuery = _context.ElectionEvents.Where(e => !e.IsDeleted);
            if (selectedTenantId.HasValue && selectedTenantId != Guid.Empty)
            {
                electionsQuery = electionsQuery.Where(e => e.TenantId == selectedTenantId);
            }

            ViewBag.ElectionEvents = new SelectList(await electionsQuery.ToListAsync(), "Id", "Title");

            ViewBag.Positions = new SelectList(await _context.Position.ToListAsync(), "Id", "Name");
            ViewBag.Parties = new SelectList(await _context.Party.ToListAsync(), "Id", "Name");
            ViewBag.States = new SelectList(await _context.States.ToListAsync(), "Id", "Name");
        }
        #endregion


        #region GetElectionsByTenant
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
        #endregion


        #region AllSystemUsers
        [HttpGet]
        public async Task<IActionResult> AllSystemUsers(string roleFilter = "", string searchTerm = "", int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = pageNumber < 1 ? 1 : pageNumber;
            pageSize = pageSize < 1 ? 10 : pageSize;

            // Unique Redis cache key considering filters and pagination
            string cacheKey = $"user_list_cache_{roleFilter}_{searchTerm}_p{pageNumber}_sz{pageSize}";

            // 1. Fetch and Cache Role-Based User Statistics & Counts
            var statsJson = await _cache.GetStringAsync(UserStatsCacheKey);
            SystemUserStatsViewModel userStats;

            if (string.IsNullOrEmpty(statsJson))
            {
                userStats = new SystemUserStatsViewModel
                {
                    TotalUsers = await _userManager.Users.CountAsync(),
                    VoterCount = (await _userManager.GetUsersInRoleAsync("Voter")).Count,
                    CandidateCount = (await _userManager.GetUsersInRoleAsync("Candidate")).Count,
                    TenantAdminCount = (await _userManager.GetUsersInRoleAsync("TenantAdmin")).Count,
                    PlatformAdminCount = (await _userManager.GetUsersInRoleAsync("PlatformAdmin")).Count,
                    SuperAdminCount = (await _userManager.GetUsersInRoleAsync("SuperAdmin")).Count
                };

                await _cache.SetStringAsync(UserStatsCacheKey, JsonSerializer.Serialize(userStats), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            else
            {
                userStats = JsonSerializer.Deserialize<SystemUserStatsViewModel>(statsJson)!;
            }

            // 2. Fetch and Cache Paginated User Results
            PaginatedListViewModel<UserWithRolesViewModel> paginatedResult;
            var cachedUserList = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedUserList))
            {
                paginatedResult = JsonSerializer.Deserialize<PaginatedListViewModel<UserWithRolesViewModel>>(cachedUserList)!;
            }
            else
            {
                var query = _userManager.Users.AsQueryable();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    query = query.Where(u => u.UserName != null && u.Email != null && (u.UserName.Contains(searchTerm) || u.Email.Contains(searchTerm)));
                }

                int totalRecords = await query.CountAsync();

                var users = await query
                    .OrderByDescending(u => u.Id)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var userListVm = new List<UserWithRolesViewModel>();
                foreach (var user in users)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    userListVm.Add(new UserWithRolesViewModel
                    {
                        Id = user.Id,
                        UserName = user.UserName ?? string.Empty,
                        Email = user.Email ?? string.Empty,
                        EmailConfirmed = user.EmailConfirmed,
                        IsLockedOut = await _userManager.IsLockedOutAsync(user),
                        Roles = roles.ToList()
                    });
                }

                // Optional post-fetch filter if a specific role was selected
                if (!string.IsNullOrEmpty(roleFilter))
                {
                    userListVm = userListVm.Where(u => u.Roles != null && u.Roles.Contains(roleFilter)).ToList();
                }

                paginatedResult = new PaginatedListViewModel<UserWithRolesViewModel>
                {
                    Items = userListVm,
                    PageNumber = pageNumber,
                    TotalItems = (int)Math.Ceiling(totalRecords / (double)pageSize),
                    TotalCount = totalRecords
                };

                // Store paginated view list in Redis for 3 minutes
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(paginatedResult), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
                });
            }

            ViewData["CurrentRoleFilter"] = roleFilter;
            ViewData["CurrentSearchTerm"] = searchTerm;
            ViewData["UserStats"] = userStats;

            return View(paginatedResult);
        }
        #endregion

        #region PenaltyLockoutUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PenaltyLockoutUser(string userId, string violationReason)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User profile not found.";
                return RedirectToAction(nameof(AllSystemUsers));
            }

            // 1. Lock out user profile
            var lockoutExpiry = DateTimeOffset.UtcNow.AddYears(100);
            var result = await _userManager.SetLockoutEndDateAsync(user, lockoutExpiry);

            if (result.Succeeded)
            {
                // 2. CREATE THE AUDIT LOG ENTRY (This feeds your Audit Log page!)
                var auditLog = new AuditLog
                {
                    UserId = user.Id,
                    Action = "PENALTY_LOCKOUT",
                    Details = $"Account penalized and locked out. Admin notice: {violationReason}",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                    Timestamp = DateTime.UtcNow
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                // 3. BROADCAST REAL-TIME VIA SIGNALR (Optional if you use a service to push it live)
                // await _hubContext.Clients.All.SendAsync("ReceiveAuditLog", auditLog);

                // 4. Send email notification to user...
                if (!string.IsNullOrEmpty(user.Email))
                {
                    string emailSubject = "Security Alert: VoteX Account Penalized & Locked";
                    string emailBody = $"Hello {user.UserName},<br><br>Your VoteX profile has been penalized and locked out.<br><strong>Reason:</strong> {violationReason}";
                    await _emailService.EmailSendAsync(user.Email, emailSubject, emailBody);
                }

                // 5. Clear Redis cache
                await _cache.RemoveAsync(UserStatsCacheKey);

                TempData["SuccessMessage"] = $"User {user.UserName} has been penalized, logged, and notified.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to apply penalty lockout.";
            }

            return RedirectToAction(nameof(AllSystemUsers));
        }
        #endregion
        #region LiftPenalty
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiftPenalty(string userId, string liftReason)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User profile not found.";
                return RedirectToAction(nameof(PenaltyLockoutUser));
            }

            // 1. Remove lockout (reset end date to null or current time)
            var result = await _userManager.SetLockoutEndDateAsync(user, null);

            if (result.Succeeded)
            {
                // 2. Log infraction lift in the Audit Trail
                var auditLog = new AuditLog
                {
                    UserId = user.Id,
                    Action = "PENALTY_LIFTED",
                    Details = $"Account penalty lifted and restriction removed. Admin notice: {liftReason}",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                    Timestamp = DateTime.UtcNow
                };
                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                // 3. Optional: Email user about restored access
                if (!string.IsNullOrEmpty(user.Email))
                {
                    string emailSubject = "Access Restored: VoteX Account Penalty Lifted";
                    string emailBody = $"Hello {user.UserName},<br><br>" +
                                       $"Your VoteX account penalty has been lifted by platform administrators and your access has been restored.<br><br>" +
                                       $"<strong>Administrator Note:</strong> {liftReason}";

                    await _emailService.EmailSendAsync(user.Email, emailSubject, emailBody);
                }

                // 4. Invalidate stats cache
                await _cache.RemoveAsync(UserStatsCacheKey);

                TempData["SuccessMessage"] = $"Penalty successfully lifted for user {user.UserName}.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to lift account penalty.";
            }

            return RedirectToAction(nameof(PenaltyLockoutUser));
        }
        #endregion
        #region PenalizedUsers
        [HttpGet]
        public async Task<IActionResult> PenalizedUsers(int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = pageNumber < 1 ? 1 : pageNumber;
            pageSize = pageSize < 1 ? 10 : pageSize;

            // Unique Redis cache key considering pagination
            string cacheKey = $"penalized_users_p{pageNumber}_sz{pageSize}";

            PaginatedListViewModel<UserWithRolesViewModel> paginatedResult;
            var cachedData = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedData))
            {
                paginatedResult = JsonSerializer.Deserialize<PaginatedListViewModel<UserWithRolesViewModel>>(cachedData)!;
            }
            else
            {
                // Filter query specifically for locked-out users
                var query = _userManager.Users.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);

                int totalRecords = await query.CountAsync();

                var users = await query
                    .OrderByDescending(u => u.LockoutEnd)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var userListVm = new List<UserWithRolesViewModel>();
                foreach (var user in users)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    userListVm.Add(new UserWithRolesViewModel
                    {
                        Id = user.Id,
                        UserName = user.UserName ?? string.Empty,
                        Email = user.Email ?? string.Empty,
                        EmailConfirmed = user.EmailConfirmed,
                        IsLockedOut = true,
                        Roles = roles.ToList()
                    });
                }

                paginatedResult = new PaginatedListViewModel<UserWithRolesViewModel>
                {
                    Items = userListVm,
                    PageNumber = pageNumber,
                    TotalItems = (int)Math.Ceiling(totalRecords / (double)pageSize),
                    TotalCount = totalRecords
                };

                // Store paginated penalized view list in Redis for 3 minutes
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(paginatedResult), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
                });
            }

            return View(paginatedResult);
        }
        #endregion
    }
}