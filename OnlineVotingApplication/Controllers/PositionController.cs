using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
    [EnableRateLimiting("StandardPolicy")]
    public class PositionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly iPositionService _positionService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditLogService _auditLogService;

        #region PositionController
        public PositionController(
            AppDbContext context,
            ITenantProvider tenantProvider,
            iPositionService positionService,
            UserManager<ApplicationUser> userManager,
            IAuditLogService auditLogService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _positionService = positionService ?? throw new ArgumentNullException(nameof(positionService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
        }
        #endregion

        #region Index
        // ─────────────────────────────────────────────
        // Index / List Positions
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index(string? electionId, int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (string.IsNullOrEmpty(electionId))
            {
                var firstElection = await _context.ElectionEvents
                    .AsNoTracking()
                    .Where(e => isSuperAdmin || e.TenantId == activeTenantId)
                    .OrderByDescending(e => e.CreatedAt)
                    .Select(e => e.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (firstElection == Guid.Empty)
                {
                    TempData["ErrorMessage"] = "No active election events found. Please create an election first.";
                    return RedirectToAction("Dashboard", "Tenant");
                }

                electionId = firstElection.ToString();
            }

            ViewBag.ElectionId = electionId;
            var paginatedPositions = await _positionService.GetAllPositionsAsync(electionId, pageNumber, pageSize);

            return View(paginatedPositions);
        }
        #endregion

        #region Create
        // ─────────────────────────────────────────────
        // Create Position
        // ─────────────────────────────────────────────
        [HttpGet]
        #region Create (GET)
        [HttpGet]
        public async Task<IActionResult> CreatePosition(Guid? electionId, CancellationToken cancellationToken = default)
        {
            await PopulateElectionsViewBagAsync(electionId, cancellationToken);
            return View(new PositionDTO { ElectionId = electionId ?? Guid.Empty });
        }
        #endregion

        #region Create (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> CreatePosition(PositionDTO model, CancellationToken cancellationToken = default)
        {
            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User session is invalid.";
                return RedirectToAction(nameof(Index));
            }

            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (model.ElectionId == Guid.Empty)
            {
                ModelState.AddModelError(nameof(model.ElectionId), "Please select a valid election event.");
            }
            else if (!isSuperAdmin)
            {
                Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
                if (activeTenantId == Guid.Empty)
                {
                    TempData["ErrorMessage"] = "No active organization context found.";
                    return RedirectToAction("SelectTenant", "Account");
                }

                var belongsToTenant = await _context.ElectionEvents
                    .AsNoTracking()
                    .AnyAsync(e => e.Id == model.ElectionId && e.TenantId == activeTenantId, cancellationToken);

                if (!belongsToTenant)
                {
                    ModelState.AddModelError(nameof(model.ElectionId), "Selected election event is invalid or unauthorized.");
                }
            }

            if (!ModelState.IsValid)
            {
                // Repopulates the dropdown so the view doesn't crash when redisplaying with errors!
                await PopulateElectionsViewBagAsync(model.ElectionId, cancellationToken);
                return View(model);
            }

            var result = await _positionService.CreatePositionAsync(model, userId, model.ElectionId);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "Failed to create position.");
                await PopulateElectionsViewBagAsync(model.ElectionId, cancellationToken);
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Position Created",
                details: $"Created position '{model.Name}' for election ID: {model.ElectionId}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = result.Message ?? "Position created successfully.";
            return RedirectToAction(nameof(Index), new { electionId = model.ElectionId });
        }
        #endregion
        #endregion

        #region PopulateElectionsViewBagAsync
        private async Task PopulateElectionsViewBagAsync(Guid? selectedElectionId = null, CancellationToken cancellationToken = default)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");
            ViewBag.IsSuperAdmin = isSuperAdmin;

            List<ElectionEvent> elections;

            if (isSuperAdmin)
            {
                // SuperAdmin gets to see ALL active, non-deleted election events across the system
                elections = await _context.ElectionEvents
                    .AsNoTracking()
                    .Where(e => !e.IsDeleted)
                    .OrderByDescending(e => e.CreatedAt)
                    .ToListAsync(cancellationToken);
            }
            else
            {
                // Regular tenant users only see elections belonging to their active tenant
                Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

                if (activeTenantId == Guid.Empty)
                {
                    elections = new List<ElectionEvent>();
                }
                else
                {
                    elections = await _context.ElectionEvents
                        .AsNoTracking()
                        .Where(e => e.TenantId == activeTenantId && !e.IsDeleted)
                        .OrderByDescending(e => e.CreatedAt)
                        .ToListAsync(cancellationToken);
                }
            }

            // Bind to ViewBag.ElectionEvents so the dropdown populates correctly in the view
            ViewBag.ElectionEvents = new SelectList(elections, "Id", "Title", selectedElectionId);
        }
        
        #endregion
        #region Edit
        // ─────────────────────────────────────────────
        // Edit Position
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var position = await _context.Position
                .Include(p => p.ElectionEvent)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

            if (position == null)
            {
                TempData["ErrorMessage"] = "The requested position could not be found.";
                return NotFound();
            }

            if (!isSuperAdmin && (position.ElectionEvent == null || position.ElectionEvent.TenantId != activeTenantId))
            {
                TempData["ErrorMessage"] = "Unauthorized access to position.";
                return RedirectToAction(nameof(Index));
            }

            var editModel = new EditPositionModel
            {
                Id = position.Id.ToString(),
                Name = position.Name ?? string.Empty
            };

            ViewBag.ElectionId = position.ElectionEventId;
            return View(editModel);
        }
        #endregion

        #region Edit (2)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> Edit(EditPositionModel model, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!Guid.TryParse(model.Id, out Guid positionId))
            {
                ModelState.AddModelError(string.Empty, "Invalid position identifier.");
                return View(model);
            }

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (!isSuperAdmin)
            {
                var positionToCheck = await _context.Position
                    .Include(p => p.ElectionEvent)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken);

                if (positionToCheck == null || positionToCheck.ElectionEvent == null || positionToCheck.ElectionEvent.TenantId != activeTenantId)
                {
                    TempData["ErrorMessage"] = "Unauthorized to edit this position.";
                    return RedirectToAction(nameof(Index));
                }
            }

            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User session is invalid.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _positionService.UpdatePosition(model, userId);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message ?? "Failed to update position.";
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Position Updated",
                details: $"Updated position ID: {model.Id} to name '{model.Name}'",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "Position updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        #endregion

        #region Delete
        // ─────────────────────────────────────────────
        // Delete Position
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> Delete(Guid id, Guid electionId, CancellationToken cancellationToken = default)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (!isSuperAdmin)
            {
                var positionToCheck = await _context.Position
                    .Include(p => p.ElectionEvent)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

                if (positionToCheck == null || positionToCheck.ElectionEvent == null || positionToCheck.ElectionEvent.TenantId != activeTenantId)
                {
                    TempData["ErrorMessage"] = "Unauthorized to delete this position.";
                    return RedirectToAction(nameof(Index), new { electionId });
                }
            }

            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User session is invalid.";
                return RedirectToAction(nameof(Index), new { electionId });
            }

            var result = await _positionService.DeletePosition(id, userId);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message ?? "Failed to delete position.";
            }
            else
            {
                // --- AUDIT LOGGING ---
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                Guid tenantId = _tenantProvider.GetCurrentTenantId();

                await _auditLogService.LogActivityAsync(
                    userId: userId,
                    action: "Position Deleted",
                    details: $"Soft-deleted position ID: {id}",
                    ipAddress: ipAddress,
                    tenantId: tenantId != Guid.Empty ? tenantId : null
                );

                TempData["SuccessMessage"] = result.Message ?? "Position deleted successfully.";
            }

            return RedirectToAction(nameof(Index), new { electionId });
        }
        #endregion

        #region AllSoftDelete
        // ─────────────────────────────────────────────
        // Soft Deleted Positions
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AllSoftDelete(int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);

            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User doesn't exist and cannot perform this function";
                return RedirectToAction("Index", "Home");
            }

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var result = await _positionService.AllSoftDeleted(pageNumber, pageSize);

            var query = _context.Position
                .Include(p => p.ElectionEvent)
                .AsNoTracking()
                .Where(m => m.IsDeleted);

            if (!isSuperAdmin)
            {
                query = query.Where(m => m.ElectionEvent != null && m.ElectionEvent.TenantId == activeTenantId);
            }

            ViewBag.SoftPositionDeletedByElection = await query
                .OrderBy(m => m.DeletedAt)
                .Take(50) // Safeguard against unbounded list size
                .ToListAsync(cancellationToken);

            var sendView = new PaginatedListViewModel<PositionDTO>
            {
                Items = result?.Items ?? new List<PositionDTO>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = result?.TotalItems ?? 0
            };

            return View(sendView);
        }
        #endregion

        #region GetAllSoftDeletePost
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GetAllSoftDeletePost(int pageNumber = 1, int pageSize = 10)
        {
            return RedirectToAction(nameof(AllSoftDelete), new { pageNumber, pageSize });
        }
        #endregion

    }
}