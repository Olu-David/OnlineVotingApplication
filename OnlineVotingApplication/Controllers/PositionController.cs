using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "Official,SuperAdmin")]
    public class PositionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly iPositionService _positionService;
        private readonly UserManager<ApplicationUser> _userManager;

        public PositionController(
            AppDbContext context,
            ITenantProvider tenantProvider,
            iPositionService positionService,
            UserManager<ApplicationUser> userManager)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _positionService = positionService ?? throw new ArgumentNullException(nameof(positionService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        // --- GET: LIST ALL POSITIONS (INDEX) ---
        [HttpGet]
        public async Task<IActionResult> Index(string? electionId, int pageNumber = 1, int pageSize = 10)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            // If no electionId is supplied, default to the first available election for this tenant/admin
            if (string.IsNullOrEmpty(electionId))
            {
                var firstElection = await _context.ElectionEvents
                    .Where(e => isSuperAdmin || e.TenantId == activeTenantId)
                    .Select(e => e.Id)
                    .FirstOrDefaultAsync();

                if (firstElection == Guid.Empty)
                {
                    TempData["ErrorMessage"] = "No active election events found. Please create an election first.";
                    return RedirectToAction("Dashboard", "Tenant");
                }

                electionId = firstElection.ToString();
            }

            ViewBag.ElectionId = electionId;

            // Fetch paginated positions via the service
            var paginatedPositions = await _positionService.GetAllPositionsAsync(electionId, pageNumber, pageSize);

            return View(paginatedPositions);
        }
        // --- GET: CREATE POSITION ---
        [HttpGet]
        public async Task<IActionResult> Create(Guid? electionId)
        {
            await PopulateElectionsViewBagAsync(electionId);
            return View(new PositionDTO { ElectionId = electionId ?? Guid.Empty });
        }

        // --- POST: CREATE POSITION ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PositionDTO model)
        {
            string userId = _userManager.GetUserId(User)!;

            if (!User.IsInRole("SuperAdmin"))
            {
                Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
                if (activeTenantId == Guid.Empty)
                {
                    TempData["ErrorMessage"] = "No active organization context found.";
                    return RedirectToAction("SelectTenant", "Account");
                }

                var belongsToTenant = await _context.ElectionEvents
                    .AnyAsync(e => e.Id == model.ElectionId && e.TenantId == activeTenantId);

                if (!belongsToTenant)
                {
                    ModelState.AddModelError("ElectionId", "Selected election event is invalid or unauthorized.");
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateElectionsViewBagAsync(model.ElectionId);
                return View(model);
            }

            var result = await _positionService.CreatePositionAsync(model, userId, model.ElectionId ?? Guid.Empty);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "Failed to create position.");
                await PopulateElectionsViewBagAsync(model.ElectionId);
                return View(model);
            }

            TempData["SuccessMessage"] = result.Message ?? "Position created successfully.";
            return RedirectToAction(nameof(Index), new { electionId = model.ElectionId });
        }

        // --- GET: EDIT POSITION ---
        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var position = await _context.Position
                .Include(p => p.ElectionEvent)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (position == null)
            {
                TempData["ErrorMessage"] = "The requested position could not be found.";
                return NotFound();
            }

            // Enforce tenant boundary check
            if (!isSuperAdmin)
            {
                if (position.ElectionEvent == null || position.ElectionEvent.TenantId != activeTenantId)
                {
                    TempData["ErrorMessage"] = "Unauthorized access to position.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var editModel = new EditPositionModel
            {
                Id = position.Id.ToString(),
                Name = position.Name ?? string.Empty
            };

            ViewBag.ElectionId = position.ElectionEventId;
            return View(editModel);
        }

        // --- POST: EDIT POSITION ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPosition(EditPositionModel model)
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

            // Enforce tenant boundary check on postback
            if (!isSuperAdmin)
            {
                var positionToCheck = await _context.Position
                    .Include(p => p.ElectionEvent)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == positionId);

                if (positionToCheck == null || positionToCheck.ElectionEvent == null || positionToCheck.ElectionEvent.TenantId != activeTenantId)
                {
                    TempData["ErrorMessage"] = "Unauthorized to edit this position.";
                    return RedirectToAction(nameof(Index));
                }
            }

            string userId = _userManager.GetUserId(User)!;

            // Fixed: Passing string userId as the second argument
            var result = await _positionService.UpdatePosition(model, userId);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message ?? "Failed to update position.";
                return View(model);
            }

            TempData["SuccessMessage"] = "Position updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        // --- POST: DELETE POSITION ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id, Guid electionId)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            // Enforce tenant boundary check before deletion
            if (!isSuperAdmin)
            {
                var positionToCheck = await _context.Position
                    .Include(p => p.ElectionEvent)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (positionToCheck == null || positionToCheck.ElectionEvent == null || positionToCheck.ElectionEvent.TenantId != activeTenantId)
                {
                    TempData["ErrorMessage"] = "Unauthorized to delete this position.";
                    return RedirectToAction(nameof(Index), new { electionId = electionId });
                }
            }

            string userId = _userManager.GetUserId(User)!;
            var result = await _positionService.DeletePosition(id, userId);

            if (!result.Success) TempData["ErrorMessage"] = result.Message;
            else TempData["SuccessMessage"] = result.Message;

            return RedirectToAction(nameof(Index), new { electionId = electionId });
        }

        // --- HELPER METHOD TO POPULATE ELECTION DROPDOWN ---
        private async Task PopulateElectionsViewBagAsync(Guid? selectedElectionId)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin") && !User.IsInRole("Official");
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var query = _context.ElectionEvents.AsNoTracking();

            if (!isSuperAdmin)
            {
                query = query.Where(e => e.TenantId == activeTenantId);
            }

            var electionList = await query
                .Select(e => new { e.Id, e.Title })
                .ToListAsync();

            ViewBag.IsSuperAdmin = isSuperAdmin;
            ViewBag.ElectionEvents = new SelectList(electionList, "Id", "Title", selectedElectionId);
        }

        // --- GET: GET ALL SOFT DELETED POSITIONS ---
        [HttpGet]
        public async Task<IActionResult> AllSoftDelete(int pageNumber = 1, int pageSize = 10)
        {
            string userId = _userManager.GetUserId(User)!;
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User doesn't exist and cannot perform this function";
                return RedirectToAction("Index", "Home");
            }

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            // Fetch from service layer
            var result = await _positionService.AllSoftDeleted(pageNumber, pageSize);

            // Fetch raw model collection for ViewBag with tenant boundary protection
            var query = _context.Position
                .Include(p => p.ElectionEvent)
                .Where(m => m.IsDeleted);

            if (!isSuperAdmin)
            {
                query = query.Where(m => m.ElectionEvent != null && m.ElectionEvent.TenantId == activeTenantId);
            }

            ViewBag.SoftPositionDeletedByElection = await query.OrderBy(m => m.DeletedAt).ToListAsync();

            var sendView = new PaginatedListViewModel<PositionDTO>
            {
                Items = result?.Items ?? new List<PositionDTO>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = result?.TotalItems ?? 0
            };

            return View(sendView);
        }

        // --- POST: GET ALL SOFT DELETED POSITIONS ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GetAllSoftDeletePost(int pageNumber = 1, int pageSize = 10)
        {
            // You can route post actions back to the Get handler or handle pagination posts cleanly
            return RedirectToAction(nameof(AllSoftDelete), new { pageNumber, pageSize });
        }
    }
}
 