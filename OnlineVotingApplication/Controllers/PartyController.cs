using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Threading.Tasks;

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
        public async Task<IActionResult> RestoreSoftDeleted(Guid PartyId)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.RestoreDeletedParty(user, PartyId);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Can not restore data try again";
                return RedirectToAction(nameof(Index));
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: user,
                action: "Party Restored",
                details: $"Restored party ID: {PartyId}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "Data restored succesfully";
            return RedirectToAction(nameof(AllParty), new { PartyId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateParty(PartyViewModel model)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
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
                return RedirectToAction(nameof(Index));
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

            TempData["SuccessMessage"] = "Party has been created successful";
            return RedirectToAction(nameof(AllParty), new { model.Id });
        }

        [HttpGet]
        public async Task<IActionResult> AllParty(int PageNumber = 1, int Pageize = 10)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.AllPartyAsync(PageNumber, Pageize);

            if (!result.Items.Any() || result.Items == null)
            {
                return View(result);
            }

            var newView = new PaginatedListViewModel<PartyViewModel>
            {
                Items = result.Items,
                PageNumber = PageNumber,
                PageSize = Pageize,
                TotalItems = result.TotalItems
            };

            return View(newView);
        }

        [HttpGet]
        public async Task<IActionResult> AllSoftDeleted(int PageNumber = 1, int Pageize = 10)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.AllSoftDeleteAsync(PageNumber, Pageize);

            if (!result.Items.Any() || result.Items == null)
            {
                return View(result);
            }

            var newView = new PaginatedListViewModel<PartyViewModel>
            {
                Items = result.Items,
                PageNumber = PageNumber,
                PageSize = Pageize,
                TotalItems = result.TotalItems
            };

            return View(newView);
        }
    }
}