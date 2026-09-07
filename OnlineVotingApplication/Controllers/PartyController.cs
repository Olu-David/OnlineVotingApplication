using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class PartyController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<PartyController> _logger;
        private readonly IPartyService _party;
        private readonly AppDbContext _context;
        private readonly IAuditLogService _auditLogService;
        private readonly ITenantProvider _tenantProvider;

        public PartyController(
            UserManager<ApplicationUser> userManager,
            ILogger<PartyController> logger,
            IPartyService party,
            AppDbContext context,
            IAuditLogService auditLogService,
            ITenantProvider tenantProvider)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult CreateParty()
        {
            return View();
        }

        [HttpGet]
        [EnableRateLimiting("StrictVotingPolicy")]
        public async Task<IActionResult> RestoreSoftDeleted(Guid partyId)
        {
            var user = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.RestoreDeletedParty(user, partyId);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Can not restore data, try again.";
                return RedirectToAction(nameof(Index));
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: user,
                action: "Party Restored",
                details: $"Restored party ID: {partyId}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "Data restored successfully.";
            return RedirectToAction(nameof(AllParty));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")]
        public async Task<IActionResult> CreateParty(PartyViewModel model)
        {
            var user = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            if (!ModelState.IsValid)
            {
                var validationErrors = string.Join(" | ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

                TempData["ErrorMessage"] = $"Unable to create. Validation Errors: {validationErrors}";
                return View(model);
            }

            var result = await _party.CreatePartyAsync(model, user);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Unable to create Party";
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: user,
                action: "Party Created",
                details: $"Created party '{model.Name}'",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "Party has been created successfully.";
            return RedirectToAction(nameof(AllParty));
        }

        [HttpGet]
        public async Task<IActionResult> AllParty(int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);

            var user = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.AllPartyAsync(pageNumber, pageSize);

            if (result?.Items == null || !result.Items.Any())
            {
                return View(result ?? new PaginatedListViewModel<PartyViewModel>());
            }

            var newView = new PaginatedListViewModel<PartyViewModel>
            {
                Items = result.Items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = result.TotalItems
            };

            return View(newView);
        }

        [HttpGet]
        public async Task<IActionResult> AllSoftDeleted(int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);

            var user = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.AllSoftDeleteAsync(pageNumber, pageSize);

            if (result?.Items == null || !result.Items.Any())
            {
                return View(result ?? new PaginatedListViewModel<PartyViewModel>());
            }

            var newView = new PaginatedListViewModel<PartyViewModel>
            {
                Items = result.Items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = result.TotalItems
            };

            return View(newView);
        }
    }
}