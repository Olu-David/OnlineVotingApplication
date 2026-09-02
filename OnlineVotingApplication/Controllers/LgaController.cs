using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class LgaController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _context;
        private readonly iLgaService _lgaService;
        private readonly IAuditLogService _auditLogService;
        private readonly ITenantProvider _tenantProvider;

        public LgaController(
            UserManager<ApplicationUser> userManager,
            AppDbContext context,
            iLgaService lgaService,
            IAuditLogService auditLogService,
            ITenantProvider tenantProvider)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _lgaService = lgaService ?? throw new ArgumentNullException(nameof(lgaService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> CreateLga()
        {
            var allStateList = await _context.States
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();

            ViewBag.State = new SelectList(allStateList, "Id", "Name");

            return View(new LgaDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLga(LgaDTO model)
        {
            if (!ModelState.IsValid)
            {
                var allStateList = await _context.States.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
                ViewBag.State = new SelectList(allStateList, "Id", "Name", model.StateId);
                return View(model);
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User is unauthorized to perform this function";
                return RedirectToAction("Index", "Home");
            }

            var result = await _lgaService.CreateLgaAsync(model, userId);
            if (!result.Success)
            {
                var allStateList = await _context.States.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
                ViewBag.State = new SelectList(allStateList, "Id", "Name", model.StateId);
                TempData["ErrorMessage"] = "LGA Creation was unsuccessful";
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "LGA Created",
                details: $"Created LGA '{model.Name}'",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "LGA created successfully.";
            return RedirectToAction(nameof(AllLga));
        }

        [HttpGet]
        public async Task<IActionResult> AllLga(int pageNumber = 1, int pageSize = 10)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User cannot perform this function.";
                return RedirectToAction("Index", "Home");
            }

            var result = await _lgaService.GetAllLgasAsync(pageNumber, pageSize);
            if (result == null || result.TotalItems == 0)
            {
                TempData["ErrorMessage"] = "Nothing was found. Try again or contact the administrator.";
                return NotFound();
            }

            var sendView = new PaginatedListViewModel<LgaDTO>
            {
                Items = result?.Items ?? new List<LgaDTO>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = result?.TotalItems ?? 0
            };

            return View(sendView);
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmDelete(Guid id)
        {
            var lgaRecord = await _context.Lgas
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (lgaRecord == null)
            {
                TempData["ErrorMessage"] = "The requested LGA could not be found.";
                return RedirectToAction(nameof(AllLga));
            }

            var model = new LgaDTO
            {
                Id = lgaRecord.Id,
                Name = lgaRecord.Name,
                StateId = lgaRecord.StateId
            };

            return View("~/Views/Lga/ConfirmDelete.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDelete(LgaDTO model)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User cannot perform the function, Authorized User only";
                return RedirectToAction("Index", "Home");
            }

            var result = await _lgaService.DeleteLgaAsync(model, userId);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Item deletion was unsuccessful";
                return RedirectToAction(nameof(AllLga));
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "LGA Deleted",
                details: $"Deleted LGA ID: {model.Id} ('{model.Name}')",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            return RedirectToAction(nameof(DeletedSuccessfully));
        }

        [HttpGet]
        public IActionResult DeletedSuccessfully()
        {
            return View("~/Views/Lga/DeletedSuccessfully.cshtml");
        }
    }
}