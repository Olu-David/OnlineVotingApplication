using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [Authorize]
    [EnableRateLimiting("StandardPolicy")]
    public class StateServiceController : Controller
    {
        private readonly iStateService _stateService;
        private readonly AppDbContext _context;
        private readonly ILogger<StateServiceController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditLogService _auditLogService;
        private readonly ITenantProvider _tenantProvider;

        public StateServiceController(
            iStateService stateService,
            AppDbContext context,
            ILogger<StateServiceController> logger,
            UserManager<ApplicationUser> userManager,
            IAuditLogService auditLogService,
            ITenantProvider tenantProvider)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet]
        public IActionResult CreateState()
        {
            return View();
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> CreateState(StateDTO model, CancellationToken cancellationToken = default)
        {
            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User not found or session expired.";
                return RedirectToAction("Index", "Home");
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Unable to create state due to validation errors.";
                return View(model);
            }

            if (model.Id == null || model.Id == Guid.Empty)
            {
                model.Id = Guid.NewGuid();
            }

            var result = await _stateService.CreateStateAsync(model, userId);
            if (!result)
            {
                TempData["ErrorMessage"] = "Unable to create state details database record.";
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "State Created",
                details: $"Created state '{model.Name}' (ID: {model.Id})",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "State created successfully!";
            return RedirectToAction(nameof(AllState));
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet]
        public async Task<IActionResult> EditState(Guid id, CancellationToken cancellationToken = default)
        {
            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User unauthorized to perform this task.";
                return RedirectToAction("Index", "Home");
            }

            var stateData = await _stateService.GetStateByIdAsync(id);
            if (stateData == null)
            {
                TempData["ErrorMessage"] = "The requested state could not be found.";
                return NotFound();
            }

            var model = new UpdateStateDto
            {
                Id = stateData.Id,
                Name = stateData.Name
            };

            return View(model);
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> EditState(Guid id, UpdateStateDto model, CancellationToken cancellationToken = default)
        {
            if (id != model.Id)
            {
                TempData["ErrorMessage"] = "State ID mismatch.";
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User session is invalid.";
                return RedirectToAction("Index", "Home");
            }

            var result = await _stateService.UpdateStateAsync(model, userId);
            if (!result)
            {
                TempData["ErrorMessage"] = "Unable to Edit State";
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "State Updated",
                details: $"Updated state ID: {model.Id} ('{model.Name}')",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "State Update Successful";
            return RedirectToAction(nameof(AllState));
        }

        [HttpGet]
        public async Task<IActionResult> AllState(CancellationToken cancellationToken = default)
        {
            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User not found";
                return RedirectToAction("Index", "Home");
            }

            int allStateCount = await _context.States.AsNoTracking().CountAsync(cancellationToken);
            ViewBag.StateCount = allStateCount;
            var result = await _stateService.GetAllStatesAsync();

            if (result == null || result.Count == 0)
            {
                TempData["ErrorMessage"] = "State search returned nothing";
            }

            return View(result);
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet]
        public IActionResult ConfirmSoftDelete()
        {
            return View();
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> ConfirmStateDelete(Guid id, CancellationToken cancellationToken = default)
        {
            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User not found / Unauthorized to perform this function";
                return RedirectToAction("Index", "Home");
            }

            var result = await _stateService.DeleteStateAsync(id);
            if (!result)
            {
                TempData["ErrorMessage"] = "State deletion unsuccessful";
                return RedirectToAction(nameof(AllState));
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "State Deleted",
                details: $"Deleted state ID: {id}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "State deleted successfully.";
            return RedirectToAction(nameof(AllState));
        }
    }
}