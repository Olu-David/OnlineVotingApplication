using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
    public class ElectionEventController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly IAuditLogService _auditLogService;
        private readonly IElectionService _electionService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ElectionEventController> _logger;
        #region ElectionEventController
        public ElectionEventController(AppDbContext context, ITenantProvider tenantProvider, IAuditLogService auditLogService, IElectionService electionService, UserManager<ApplicationUser> userManager, ILogger<ElectionEventController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _electionService = electionService ?? throw new ArgumentNullException(nameof(electionService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Index
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
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var query = _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(e => e.Tenant)
                .Where(e => !e.IsDeleted);

            if (!isSuperAdmin)
            {
                query = query.Where(e => e.TenantId == activeTenantId);
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
        #endregion

        #region Create
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
        #endregion

        #region Create (2)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")]
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
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            Guid? effectiveTenantId = isSuperAdmin ? model.TenantId : activeTenantId;

            if (!isSuperAdmin && effectiveTenantId == Guid.Empty)
            {
                TempData["ErrorMessage"] = "Active organization context not found.";
                return RedirectToAction(nameof(Index));
            }
            var election = new ElectionEvent
            {
                Id = Guid.NewGuid(),
                Title = model?.Title ?? "",
                ElectionYear = model!.ElectionYear,

                // ─── CONVERTED TO UTC FOR POSTGRESQL ───────────────
                StartDate = DateTime.SpecifyKind(model.StartDate, DateTimeKind.Utc),
                EndDate = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Utc),
                // ───────────────────────────────────────────────────

                IsActive = model.IsActive,
                Category = model.Category,
                TenantId = effectiveTenantId,
                CreatedAt = DateTime.UtcNow
            };

            _context.ElectionEvents.Add(election);
            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Created",
                details: $"Created election event '{election.Title}' (ID: {election.Id})",
                ipAddress: ipAddress,
                tenantId: effectiveTenantId != Guid.Empty ? effectiveTenantId : null
            );

            _logger.LogInformation("Election {ElectionId} ('{Title}') created by {User} for TenantId={TenantId}",
                election.Id, election.Title, User.Identity?.Name, election.TenantId);

            TempData["SuccessMessage"] = $"Election \"{election.Title}\" created successfully.";
            return RedirectToAction(nameof(Index));
        }
        #endregion

        #region Edit (GET)
        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var query = _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking();

            ElectionEvent? election = isSuperAdmin
                ? await query.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted)
                : await query.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted && e.TenantId == activeTenantId);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event could not be found or unauthorized access.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateFormDataAsync();

            var model = new ElectionViewModel
            {
                Id = election.Id,
                Title = election.Title,
                Description = election.Description,
                ElectionYear = election.ElectionYear,
                StartDate = election.StartDate,
                EndDate = election.EndDate,
                IsActive = election.IsActive,
                Category = election.Category,
                TenantId = election.TenantId,
                PhotoImage = election.ImageUrl // Populates the image preview for the view
            };

            return View(model);
        }
        #endregion

        #region Edit (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")]
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

            var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            // Map ViewModel to DTO to send safely to the service layer
            var dto = new ElectionDto
            {
                Id = model.Id,
                Title = model.Title,
                Description = model.Description,
                StartDate = DateTime.SpecifyKind(model.StartDate, DateTimeKind.Utc),
                EndDate = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Utc),
                UrlImage = model.UrlImage, // Captures any newly uploaded image file
                Category = model.Category,
                TenantId = model.TenantId
            };

            var result = await _electionService.EditElectionAsync(dto, userId);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message??"An error occurred while updating the election.");
                await PopulateFormDataAsync();
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var activeTenantId = _tenantProvider.GetCurrentTenantId();
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Updated",
                details: $"Updated election event ID: {model.Id} ('{model.Title}')",
                ipAddress: ipAddress,
                tenantId: activeTenantId != Guid.Empty ? activeTenantId : null
            );

            TempData["SuccessMessage"] = $"Election \"{model.Title}\" updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        #endregion

        #region SoftDelete
        // ─────────────────────────────────────────────
        // POST: SoftDelete / GET: SoftDeleted / POST: Restore
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")]
        public async Task<IActionResult> RestoreSoftDelete(Guid id)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var query = _context.ElectionEvents
                .IgnoreQueryFilters();

            ElectionEvent? election = isSuperAdmin
                ? await query.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted)
                : await query.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted && e.TenantId == activeTenantId);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event could not be found or unauthorized access.";
                return RedirectToAction(nameof(Index));
            }

            election.IsDeleted = true;
            election.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
            var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Soft-Deleted",
                details: $"Moved election event ID: {id} ('{election.Title}') to trash",
                ipAddress: ipAddress,
                tenantId: activeTenantId != Guid.Empty ? activeTenantId : null
            );

            TempData["SuccessMessage"] = $"\"{election.Title}\" moved to trash. It can be restored within 30 days.";
            return RedirectToAction(nameof(Index));
        }
        #endregion

        #region SoftDeleted
        [HttpGet]
        public async Task<IActionResult> SoftDelete(int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            var retentionThreshold = DateTime.UtcNow.AddDays(-30);
            bool isSuperAdmin = User.IsInRole("SuperAdmin");
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var query = _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(e => e.Tenant)
                .Where(e => e.IsDeleted && e.DeletedAt >= retentionThreshold);

            if (!isSuperAdmin)
            {
                query = query.Where(e => e.TenantId == activeTenantId);
            }

            int totalItems = await query.CountAsync();

            var items = await query
                .OrderByDescending(e => e.DeletedAt)
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
        #endregion

        #region Restore
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")]
        public async Task<IActionResult> Restore(Guid id)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var query = _context.ElectionEvents
                .IgnoreQueryFilters();

            ElectionEvent? election = isSuperAdmin
                ? await query.FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted)
                : await query.FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted && e.TenantId == activeTenantId);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event could not be found in trash or unauthorized access.";
                return RedirectToAction(nameof(SoftDelete));
            }

            if (!election.DeletedAt.HasValue)
            {
                TempData["ErrorMessage"] = "Invalid deletion date.";
                return RedirectToAction(nameof(SoftDelete));
            }

            double daysSinceDeleted = (DateTime.UtcNow - election.DeletedAt.Value).TotalDays;
            if (daysSinceDeleted > 30)
            {
                TempData["ErrorMessage"] = "This election can no longer be restored (past the 30-day window).";
                return RedirectToAction(nameof(SoftDelete));
            }

            election.IsDeleted = false;
            election.DeletedAt = null;
            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
            var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Election Restored",
                details: $"Restored election event ID: {id} ('{election.Title}') from trash",
                ipAddress: ipAddress,
                tenantId: activeTenantId != Guid.Empty ? activeTenantId : null
            );

            TempData["SuccessMessage"] = $"\"{election.Title}\" restored successfully.";
            return RedirectToAction(nameof(Index));
        }
        #endregion

        #region PopulateFormDataAsync
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
        #endregion
    }
}