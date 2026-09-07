using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Controllers
{
    [Authorize]
    public class TenantController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly ITenantService _tenantService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditLogService _auditLogService;
        private readonly iCandidateService _candidateService;

        public TenantController(
            AppDbContext context,
            ITenantProvider tenantProvider,
            ITenantService tenantService,
            UserManager<ApplicationUser> userManager,
            IAuditLogService auditLogService,
            iCandidateService candidateService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _tenantService = tenantService ?? throw new ArgumentNullException(nameof(tenantService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _candidateService = candidateService ?? throw new ArgumentNullException(nameof(candidateService));
        }

        // ─────────────────────────────────────────────
        // Registration (Public Access)
        // ─────────────────────────────────────────────
        [AllowAnonymous]
        [HttpGet]
        public IActionResult CreateOrganization()
        {
            return View(new TenantRegistrationViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")]
        public async Task<IActionResult> CreateOrganization(TenantRegistrationViewModel model, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await _tenantService.RegisterTenantOrganizationAsync(model);

            if (!result.Success)
            {
                if (result.Errors?.Any() == true)
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error ?? "An unknown registration error occurred.");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, result.Message ?? "An error occurred while creating your organization profile.");
                }
                return View(model);
            }

            if (result.Data?.Tenant != null)
            {
                _tenantProvider.SetTenantContext(result.Data.Tenant.Id);
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            string userId = result.Data?.UserId ?? "Anonymous";
            Guid? tenantId = result.Data?.Tenant?.Id;

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Organization Registered",
                details: $"Registered organization profile '{model.OrganizationName}'",
                ipAddress: ipAddress,
                tenantId: tenantId
            );

            TempData["SuccessMessage"] = result.Message ?? "Organization successfully provisioned.";

            return RedirectToRoute(new
            {
                area = "Identity",
                controller = "Account",
                action = "ConfirmEmailSent",
                email = model.AdminEmail
            });
        }

        // ─────────────────────────────────────────────
        // Tenant Dashboard & Management
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Dashboard(CancellationToken cancellationToken = default)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            if (activeTenantId == Guid.Empty)
            {
                if (User.IsInRole("SuperAdmin"))
                {
                    return RedirectToAction("AllTenants", "SuperAdminDashboard");
                }
                return RedirectToAction("SelectTenant", "Account");
            }

            var tenantDetails = await _tenantService.GetTenantDetailsAsync();
            if (tenantDetails == null || !tenantDetails.IsActive)
            {
                TempData["ErrorMessage"] = "Selected organization is inactive or non-existent.";
                return RedirectToAction("AllTenants", "SuperAdminDashboard");
            }

            var metrics = await _tenantService.GetDashboardMetricsAsync();

            var elections = await _context.ElectionEvents
                .Where(e => e.TenantId == activeTenantId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            ViewBag.Metrics = metrics;
            ViewBag.Tenant = tenantDetails;

            return View(elections);
        }

        [HttpGet]
        [Authorize(Roles = "Official,SuperAdmin")]
        public async Task<IActionResult> ManageUsers(CancellationToken cancellationToken = default)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            if (activeTenantId == Guid.Empty)
            {
                return RedirectToAction(User.IsInRole("SuperAdmin") ? "AllTenants" : "SelectTenant", User.IsInRole("SuperAdmin") ? "SuperAdminDashboard" : "Account");
            }

            var admins = await _tenantService.GetTenantAdminsAsync();
            return View(admins);
        }

        [HttpGet]
        [Authorize(Roles = "Official,SuperAdmin")]
        public async Task<IActionResult> ElectionAnalytics(Guid electionId, CancellationToken cancellationToken = default)
        {
            if (electionId == Guid.Empty) return BadRequest();

            var candidates = await _tenantService.GetElectionCandidatesAsync(electionId);
            var totalVoters = await _tenantService.GetElectionVoterCountAsync(electionId);

            ViewBag.TotalVoters = totalVoters;
            ViewBag.ElectionId = electionId;

            return View(candidates);
        }

        [HttpGet]
        [Authorize(Roles = "Official")]
        public async Task<IActionResult> CreateCandidateOfficial(CancellationToken cancellationToken = default)
        {
            Guid currentTenantId = _tenantProvider.GetCurrentTenantId();

            ViewBag.OfficialElections = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e => e.TenantId == currentTenantId && !e.IsDeleted)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync(cancellationToken);

            return View(new ManualCandidateCreationViewModel());
        }

        [HttpPost]
        [Authorize(Roles = "Official")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictVotingPolicy")]
        public async Task<IActionResult> CreateCandidateOfficial(ManualCandidateCreationViewModel model, CancellationToken cancellationToken = default)
        {
            Guid currentTenantId = _tenantProvider.GetCurrentTenantId();

            if (!ModelState.IsValid)
            {
                ViewBag.OfficialElections = await _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(e => e.TenantId == currentTenantId && !e.IsDeleted)
                    .OrderByDescending(e => e.CreatedAt)
                    .ToListAsync(cancellationToken);

                return View(model);
            }

            string? officialUserId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(officialUserId))
            {
                TempData["ErrorMessage"] = "User session is invalid.";
                return RedirectToAction(nameof(Dashboard));
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            var result = await _candidateService.CreateCandidateByOfficialAsync(model, currentTenantId, officialUserId);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? string.Empty);

                ViewBag.OfficialElections = await _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(e => e.TenantId == currentTenantId && !e.IsDeleted)
                    .OrderByDescending(e => e.CreatedAt)
                    .ToListAsync(cancellationToken);

                return View(model);
            }

            await _auditLogService.LogActivityAsync(
                userId: officialUserId,
                action: "Official Candidate Creation",
                details: $"Official added candidate '{model.CandidateEmail}'",
                ipAddress: ipAddress,
                tenantId: currentTenantId != Guid.Empty ? currentTenantId : null
            );

            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction("AllCandidate");
        }
    }
}