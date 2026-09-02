using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Controllers
{
    public class StateServiceController : Controller
    {
        private readonly iStateService _StateService;
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
            _StateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        }

        public IActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpGet]
        public IActionResult CreateState()
        {
            return View();
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateState(StateDTO model)
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

                foreach (var item in ModelState)
                {
                    var fieldname = item.Key;
                    var errors = item.Value.Errors;

                    foreach (var error in errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"Field: {fieldname} - Error: {error.ErrorMessage}");
                    }
                }
                return View(model);
            }

            if (model.Id == null)
            {
                model.Id = Guid.NewGuid();
            }

            var result = await _StateService.CreateStateAsync(model, userId);
            if (result == false)
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
            return RedirectToAction(nameof(AllState), new { id = model.Id });
        }

        [HttpGet]
        public async Task<IActionResult> EditState(Guid id)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User Unauthorized to perform this task. Only Admins or Officials can.";
                return RedirectToAction("Index", "Home");
            }

            var stateData = await _StateService.GetStateByIdAsync(id);
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditState(string Id, UpdateStateDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _StateService.UpdateStateAsync(model, Id);
            if (!result)
            {
                TempData["ErrorMessage"] = "Unable to Edit State";
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "State Updated",
                details: $"Updated state ID: {model.Id} ('{model.Name}')",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SucessMessage"] = "State Update Successful";
            return RedirectToAction(nameof(AllState), new { model.Id });
        }

        [HttpGet]
        public async Task<IActionResult> AllState()
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not Found";
                return RedirectToAction("Index", "Home");
            }

            int Allstate = await _context.States.CountAsync();
            ViewBag.StateCount = Allstate;
            var result = await _StateService.GetAllStatesAsync();

            if (result.Count == 0)
            {
                TempData["ErrorMessage"] = "State search returned nothing";
                return View(result);
            }

            return View(result);
        }

        [HttpGet]
        public IActionResult ConfirmSoftDelete()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmStateDelete(Guid Id)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found/Unathorized to perform this function";
                return RedirectToAction("Index", "Home");
            }

            var result = await _StateService.DeleteStateAsync(Id);
            if (!result)
            {
                TempData["ErrorMessage"] = "State deleted unsuccessful";
                return View();
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: user,
                action: "State Deleted",
                details: $"Deleted state ID: {Id}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            return View(nameof(AllState));
        }
    }
}