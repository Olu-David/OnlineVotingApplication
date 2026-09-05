using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting; // Required for rate limiting attributes
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin,Official")]
    public class ElectionEventController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly IAuditLogService _auditLogService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ElectionEventController> _logger;

        public ElectionEventController(
            AppDbContext context,
            ITenantProvider tenantProvider,
            IAuditLogService auditLogService,
            UserManager<ApplicationUser> userManager,
            ILogger<ElectionEventController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ─────────────────────────────────────────────
        // GET: Election (list, search, filter, paginate)
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index(string? search, TenantCategory? category, int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var query = _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(e => e.Tenant)
                .Where(e => !e.IsDeleted);

            if (!isSuperAdmin)
            {
                var tenantId = _tenantProvider.GetCurrentTenantId();
                query = query.Where(e => e.TenantId == tenantId);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(e => e.Title != null && e.Title.Contains(search));
            }

            if (category.HasValue)
            {
                query = query.Where(e => e.Category == category.Value);
            }

            int totalItems = await query.CountAsync();

            var items = await query
                .OrderByDescending(e => e.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(e => new ElectionViewModel
                {
                    Id = e.Id,
                    Title = e.Title ?? "",
                    ElectionYear = e.ElectionYear,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    IsActive = e.IsActive,
                    Category = e.Category,
                    TenantId = e.TenantId,
                    TenantName = e.Tenant != null ? e.Tenant.OrganizationName : "System-wide"
                })
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Category = category;

            var viewModel = new PaginatedListViewModel<ElectionViewModel>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            return View(viewModel);
        }

        // ─────────────────────────────────────────────
        // GET/POST: Create
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateFormDataAsync();

            var model = new ElectionViewModel
            {
                ElectionYear = DateTime.UtcNow.Year,
                StartDate = DateTime.UtcNow.Date,
                EndDate = DateTime.UtcNow.Date.AddDays(1)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects against rapid creation spam
        public async Task<IActionResult> Create(ElectionViewModel model)
        {
            if (model.EndDate <= model.StartDate)
            {
                ModelState.AddModelError(nameof(model.EndDate), "End date must be after the start date.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateFormDataAsync();
                return View(model);
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User session is invalid.";
                return RedirectToAction(nameof(Index));
            }

            bool isSuperAdmin = User.IsInRole("SuperAdmin");
            Guid? effectiveTenantId = isSuperAdmin ? model.TenantId : _tenantProvider.GetCurrentTenantId();

            var election = new ElectionEvent
            {
                Id = Guid.NewGuid(),
                Title = model.Title,
                ElectionYear = model.ElectionYear,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                IsActive = model.IsActive,
                Category = model.Category,
                TenantId = effectiveTenantId,
                CreatedAt = DateTime.UtcNow
            };

            _context.ElectionEvents.Add(election);
            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Created",
                details: $"Created election event '{election.Title}' (ID: {election.Id})",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            _logger.LogInformation("Election {ElectionId} ('{Title}') created by {User} for TenantId={TenantId}",
                election.Id, election.Title, User.Identity?.Name, election.TenantId);

            TempData["SuccessMessage"] = $"Election \"{election.Title}\" created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // GET/POST: Edit
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event could not be found.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateFormDataAsync();

            var model = new ElectionViewModel
            {
                Id = election.Id,
                Title = election.Title,
                ElectionYear = election.ElectionYear,
                StartDate = election.StartDate,
                EndDate = election.EndDate,
                IsActive = election.IsActive,
                Category = election.Category,
                TenantId = election.TenantId
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects against excessive edits/spam
        public async Task<IActionResult> Edit(ElectionViewModel model)
        {
            if (model.EndDate <= model.StartDate)
            {
                ModelState.AddModelError(nameof(model.EndDate), "End date must be after the start date.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateFormDataAsync();
                return View(model);
            }

            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == model.Id && !e.IsDeleted);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event could not be found.";
                return RedirectToAction(nameof(Index));
            }

            election.Title = model.Title;
            election.ElectionYear = model.ElectionYear;
            election.StartDate = model.StartDate;
            election.EndDate = model.EndDate;
            election.IsActive = model.IsActive;
            election.Category = model.Category;

            if (User.IsInRole("SuperAdmin"))
            {
                election.TenantId = model.TenantId;
            }

            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
            var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Updated",
                details: $"Updated election event ID: {model.Id} ('{model.Title}')",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = $"Election \"{election.Title}\" updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // POST: SoftDelete / GET: SoftDeleted / POST: Restore
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects delete actions from abuse
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event could not be found.";
                return RedirectToAction(nameof(Index));
            }

            election.IsDeleted = true;
            election.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
            var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Soft-Deleted",
                details: $"Moved election event ID: {id} ('{election.Title}') to trash",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = $"\"{election.Title}\" moved to trash. It can be restored within 30 days.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> SoftDeleted(int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            var retentionThreshold = DateTime.UtcNow.AddDays(-30);

            var query = _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(e => e.Tenant)
                .Where(e => e.IsDeleted && e.DeletedAt >= retentionThreshold);

            if (!User.IsInRole("SuperAdmin"))
            {
                var tenantId = _tenantProvider.GetCurrentTenantId();
                query = query.Where(e => e.TenantId == tenantId);
            }

            int totalItems = await query.CountAsync();

            var items = await query
                .OrderByDescending(e => e.DeletedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(e => new ElectionViewModel
                {
                    Id = e.Id,
                    Title = e.Title,
                    ElectionYear = e.ElectionYear,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    IsActive = e.IsActive,
                    Category = e.Category,
                    TenantId = e.TenantId,
                    TenantName = e.Tenant != null ? e.Tenant.OrganizationName : "System-wide",
                    DeletedAt = e.DeletedAt
                })
                .ToListAsync();

            var viewModel = new PaginatedListViewModel<ElectionViewModel>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")] // Protects restore actions from abuse
        public async Task<IActionResult> Restore(Guid id)
        {
            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event could not be found in trash.";
                return RedirectToAction(nameof(SoftDeleted));
            }

            if (!election.DeletedAt.HasValue)
            {
                TempData["ErrorMessage"] = "Invalid deletion date.";
                return RedirectToAction(nameof(SoftDeleted));
            }

            double daysSinceDeleted = (DateTime.UtcNow - election.DeletedAt.Value).TotalDays;
            if (daysSinceDeleted > 30)
            {
                TempData["ErrorMessage"] = "This election can no longer be restored (past the 30-day window).";
                return RedirectToAction(nameof(SoftDeleted));
            }

            election.IsDeleted = false;
            election.DeletedAt = null;
            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
            var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Restored",
                details: $"Restored election event ID: {id} ('{election.Title}') from trash",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = $"\"{election.Title}\" restored successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // Helper: dropdown data for Create/Edit
        // ─────────────────────────────────────────────
        private async Task PopulateFormDataAsync()
        {
            ViewBag.Categories = new SelectList(Enum.GetValues(typeof(TenantCategory)));

            if (User.IsInRole("SuperAdmin"))
            {
                var tenants = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => t.IsActive)
                    .OrderBy(t => t.OrganizationName)
                    .ToListAsync();

                ViewBag.Tenants = new SelectList(tenants, "Id", "OrganizationName");
            }
        }
    }
}