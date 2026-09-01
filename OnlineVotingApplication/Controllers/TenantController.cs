using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
    [Authorize]
    public class TenantController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly ITenantService _tenantService;
        private readonly UserManager<ApplicationUser> _userManager;

        public TenantController(
            AppDbContext context,
            ITenantProvider tenantProvider,
            ITenantService tenantService,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _tenantProvider = tenantProvider;
            _tenantService = tenantService;
            _userManager = userManager;
        }

        // --- REGISTRATION (Public Access) ---

        [AllowAnonymous]
        [HttpGet]
        public IActionResult CreateOrganization()
        {
            return View(new TenantRegistrationViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrganization(TenantRegistrationViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

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

            TempData["SuccessMessage"] = result.Message ?? "Organization successfully provisioned.";

            return RedirectToAction("ConfirmEmail", "Account", new
            {
                area = "Identity",
                userId = result.Data?.UserId,
                code = result.Data?.Token
            });
        }

        // --- TENANT DASHBOARD & MANAGEMENT (Official / SuperAdmin) ---

        [HttpGet]
        public async Task<IActionResult> Dashboard()
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
                .ToListAsync();

            ViewBag.Metrics = metrics;
            ViewBag.Tenant = tenantDetails;

            return View(elections);
        }

        [HttpGet]
        [Authorize(Roles = "Official,SuperAdmin")]
        public async Task<IActionResult> ManageUsers()
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
        public async Task<IActionResult> ElectionAnalytics(Guid electionId)
        {
            if (electionId == Guid.Empty) return BadRequest();

            var candidates = await _tenantService.GetElectionCandidatesAsync(electionId);
            var totalVoters = await _tenantService.GetElectionVoterCountAsync(electionId);

            ViewBag.TotalVoters = totalVoters;
            ViewBag.ElectionId = electionId;

            return View(candidates);
        }
    }
}