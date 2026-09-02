using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "SuperAdmin, Admin")]
    public class CandidateController : Controller
    {
        private readonly AppDbContext _context;
        private readonly iCandidateService _candidateService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<CandidateController> _logger;
        private readonly IEmailService _emailService;
        private readonly IAuditLogService _auditLogService;
        private readonly ITenantProvider _tenantProvider;

        public CandidateController(
            AppDbContext context,
            iCandidateService candidateService,
            UserManager<ApplicationUser> userManager,
            ILogger<CandidateController> logger,
            IEmailService emailService,
            IAuditLogService auditLogService,
            ITenantProvider tenantProvider)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _candidateService = candidateService ?? throw new ArgumentNullException(nameof(candidateService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        }

        // ─────────────────────────────────────────────
        // CANDIDATE DASHBOARD (INDEX) - TENANT AWARE
        // ─────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "Candidate,SuperAdmin,Official")]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "AuthService");
            }

            if (User.IsInRole("Candidate"))
            {
                var candidateRecord = await _context.Candidate
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(c => c.Party)
                    .Include(c => c.Position)
                        .ThenInclude(p => p!.ElectionEvent)
                    .Include(c => c.State)
                    .Include(c => c.LGA)
                    .FirstOrDefaultAsync(c => c.UserId == userId && !c.isDeleted);

                if (candidateRecord == null)
                {
                    TempData["ErrorMessage"] = "No candidate profile is currently linked to your user account.";
                    return View("ProfileNotFound");
                }

                var candidateVm = new CandidateViewModel
                {
                    CandidateID = candidateRecord.Id,
                    Name = candidateRecord.Name,
                    Manifesto = candidateRecord.Manifesto,
                    PartyId = candidateRecord.PartyId,
                    PartyName = candidateRecord.Party?.Name,
                    PositionId = candidateRecord.PositionId,
                    PositionName = candidateRecord.Position?.Name,
                    StateId = candidateRecord.StateId,
                    StateName = candidateRecord.State?.Name,
                    LgaId = candidateRecord.LgaId,
                    LgaName = candidateRecord.LGA?.Name,
                    ElectionEventId = candidateRecord.Position?.ElectionEventId ?? Guid.Empty
                };

                ViewBag.ElectionTitle = candidateRecord.Position?.ElectionEvent?.Title;
                ViewBag.TenantId = candidateRecord.Position?.ElectionEvent?.TenantId;

                return View("Index", candidateVm);
            }

            return RedirectToAction(nameof(AllCandidate));
        }

        // ─────────────────────────────────────────────
        // STEP A: ADMIN GENERATES AND SENDS THE INVITE
        // ─────────────────────────────────────────────
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,Official")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendCandidateInvite(SendCandidateInvitation model)
        {
            if (string.IsNullOrWhiteSpace(model.CandidateEmail))
            {
                TempData["ErrorMessage"] = "Candidate email is required.";
                return RedirectToAction("ElectionDetails", "Election", new { id = model.ElectionEventId });
            }

            var cleanEmail = model.CandidateEmail.Trim().ToLower();

            var uniqueToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            string? secureLink = Url.Action(
                action: "CreateCandidate",
                controller: "Candidate",
                values: new { token = uniqueToken },
                protocol: Request.Scheme
            );

            if (string.IsNullOrEmpty(secureLink))
            {
                TempData["ErrorMessage"] = "Could not generate secure invitation link.";
                return RedirectToAction("ElectionDetails", "Election", new { id = model.ElectionEventId });
            }

            var result = await _candidateService.SendCandidateInviteAsync(model);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message;
            }
            else
            {
                // --- AUDIT LOGGING ---
                string userId = _userManager.GetUserId(User)!;
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                Guid tenantId = _tenantProvider.GetCurrentTenantId();

                await _auditLogService.LogActivityAsync(
                    userId: userId,
                    action: "Candidate Invite Sent",
                    details: $"Sent invitation to '{cleanEmail}' for election ID: {model.ElectionEventId}",
                    ipAddress: ipAddress,
                    tenantId: tenantId != Guid.Empty ? tenantId : null
                );

                TempData["SuccessMessage"] = result.Message;
            }

            return RedirectToAction("ElectionDetails", "Election", new { id = model.ElectionEventId });
        }

        [HttpGet]
        public async Task<IActionResult> CreateCandidate(Guid electionEventId)
        {
            _logger.LogInformation("CreateCandidate GET called with electionEventId={ElectionEventId}", electionEventId);

            if (electionEventId == Guid.Empty)
            {
                var latestElection = await _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(e => !e.IsDeleted)
                    .OrderByDescending(e => e.CreatedAt)
                    .FirstOrDefaultAsync();

                if (latestElection != null)
                {
                    electionEventId = latestElection.Id;
                }
                else
                {
                    TempData["ErrorMessage"] = "No elections found in the system. Please create an election first.";
                    return RedirectToAction("AllElections", "Election");
                }
            }

            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == electionEventId && !e.IsDeleted);

            if (election == null)
            {
                _logger.LogWarning("No active ElectionEvent found for Id={ElectionEventId}", electionEventId);
                TempData["ErrorMessage"] = "Specified election event could not be found or has been deleted.";
                return RedirectToAction("AllElections", "Election");
            }

            await PopulateCreateDropdownsAsync(election, null);

            var viewModel = new CandidateViewModel
            {
                ElectionEventId = electionEventId
            };

            return View(viewModel);
        }

        // ─────────────────────────────────────────────
        // POST: Create Candidate (Passes Token to Service)
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCandidate(CandidateViewModel model, string token)
        {
            if (model == null || model.ElectionEventId == Guid.Empty)
            {
                TempData["ErrorMessage"] = "Invalid candidate payload submitted.";
                return RedirectToAction("AllElections", "Election");
            }

            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .Include(e => e.CustomFields)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == model.ElectionEventId && !e.IsDeleted);

            if (election == null)
            {
                _logger.LogWarning("Targeted election event not found for Id={ElectionEventId}", model.ElectionEventId);
                TempData["ErrorMessage"] = "Targeted election event could not be found.";
                return RedirectToAction("AllElections", "Election");
            }

            if (election.Category != Enums.TenantCategory.Political)
            {
                ModelState.Remove("PartyId");
                model.PartyId = null;
            }

            if (!ModelState.IsValid)
            {
                var validationErrors = string.Join(" | ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

                _logger.LogWarning("Validation failed for ElectionId {ElectionId}: {Errors}",
                    model.ElectionEventId, validationErrors);

                TempData["ErrorMessage"] = $"Validation Errors: {validationErrors}";

                ModelState.SetModelValue(nameof(model.ElectionEventId), new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult(model.ElectionEventId.ToString()));

                ViewBag.InviteToken = token;
                await PopulateCreateDropdownsAsync(election, model.StateId);
                return View(model);
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Unauthenticated user context encountered during candidate post.");
                TempData["ErrorMessage"] = "Your session has expired. Please log in again.";
                return RedirectToAction("Login", "Account");
            }

            var result = await _candidateService.CreateCandidateAsync(model, userId, token);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "An error occurred while saving candidate.");
                TempData["ErrorMessage"] = result.Message;

                ModelState.SetModelValue(nameof(model.ElectionEventId), new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult(model.ElectionEventId.ToString()));

                ViewBag.InviteToken = token;
                await PopulateCreateDropdownsAsync(election, model.StateId);
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Candidate Created",
                details: $"Created candidate profile '{model.Name}' for election ID: {model.ElectionEventId}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(AllCandidate));
        }

        // ─────────────────────────────────────────────
        // AJAX Endpoints
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<JsonResult> GetLgasByState(Guid stateId)
        {
            var lgas = await _context.Lgas
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(l => l.StateId == stateId)
                .Select(l => new { id = l.Id, name = l.Name })
                .ToListAsync();

            return Json(lgas);
        }

        // ─────────────────────────────────────────────
        // Soft Delete & Restore
        // ─────────────────────────────────────────────
        [HttpGet]
        public IActionResult SoftDelete()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Home");

            var result = await _candidateService.SoftDeleteCandidateAsync(id, userId);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Unable to delete Candidate Successfully";
                return RedirectToAction(nameof(GetAllSoftdelete));
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Candidate Soft-Deleted",
                details: $"Soft-deleted candidate ID: {id}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "Candidate Moved to Trash, You can restore after 30 days";
            return View();
        }

        [HttpGet]
        public IActionResult RestoreCandidate()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreCandidate(Guid id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Home");

            var result = await _candidateService.RestoreCandidateDeleteAsync(id, userId);
            if (!result.Success)
            {
                return RedirectToAction(nameof(GetAllSoftdelete));
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Candidate Restored",
                details: $"Restored candidate ID: {id}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            return RedirectToAction(nameof(AllCandidate));
        }

        // ─────────────────────────────────────────────
        // Queries & Filters
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetCandidateByPosition(Guid? PositionId, int PageNumber = 1, int PageSize = 10)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (user == null)
            {
                return RedirectToAction("Index", "Home");
            }
            if (PageNumber < 1) PageNumber = 1;
            if (PageSize < 1) PageSize = 10;

            var PositionList = await _context.Position.AsNoTracking().ToListAsync();
            ViewBag.Position = new SelectList(PositionList, "Id", "Name", PositionId);

            var ViewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = Enumerable.Empty<CandidateViewModel>(),
                PageNumber = PageNumber,
                PageSize = PageSize,
                TotalItems = PositionList.Count
            };

            if (PositionId.HasValue && PositionId != Guid.Empty)
            {
                var result = await _candidateService.GetCandidateByPositionAsync(PositionId.Value, PageNumber, PageSize);

                if (result.Success && result.Data != null)
                {
                    ViewModel.Items = result.Data;
                    ViewModel.TotalItems = result.TotalCount;

                    var Selected = PositionList.FirstOrDefault(m => m.Id == PositionId.Value);
                    ViewBag.PositionName = Selected != null ? Selected.Name : "";
                    ViewBag.Id = Selected?.Id;
                }
            }
            return View(ViewModel);
        }

        [HttpGet]
        public async Task<IActionResult> AllCandidate(int pageNumber = 1, int pageSize = 10)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var result = await _candidateService.GetAllCandidates(pageNumber, pageSize);

            var viewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = result.Items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = result.TotalItems
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetCandidateByState(Guid? stateId, int pageNumber = 1, int pageSize = 10)
        {
            var statesList = await _context.States.AsNoTracking().ToListAsync();
            ViewBag.States = new SelectList(statesList, "Id", "Name", stateId);

            var viewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = Enumerable.Empty<CandidateViewModel>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = 0
            };

            if (stateId.HasValue && stateId.Value != Guid.Empty)
            {
                var result = await _candidateService.GetCandidateByStateAsync(stateId.Value, pageNumber, pageSize);

                if (result.Success && result.Data != null)
                {
                    viewModel.Items = result.Data;
                    viewModel.TotalItems = result.TotalCount;

                    var selectedState = statesList.FirstOrDefault(s => s.Id == stateId.Value);
                    ViewBag.SelectedStateName = selectedState?.Name;
                    ViewBag.SelectedStateId = stateId.Value;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }
            }

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSoftdelete(int pageNumber = 1, int pageSize = 10)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Index", "Home");
            }

            var result = await _candidateService.GetAllSoftDeletedCandidate(userId, pageNumber, pageSize);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction("Index", "Home");
            }

            var retentionThreshold = DateTime.UtcNow.AddDays(-30);
            int totalTrashItems = await _context.Candidate
                .CountAsync(m => m.isDeleted && m.DeletedAt >= retentionThreshold);

            var viewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = result.Data ?? Enumerable.Empty<CandidateViewModel>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalTrashItems
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetCandidateByLga(Guid? Lgaid, int PageNumber = 1, int pageSize = 10)
        {
            if (PageNumber < 1) PageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var LgaList = await _context.Lgas.AsNoTracking().ToListAsync();
            ViewBag.Lga = new SelectList(LgaList, "Id", "Name", Lgaid);

            var ViewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = new List<CandidateViewModel>(),
                PageNumber = PageNumber,
                PageSize = pageSize,
                TotalItems = 0
            };

            if (Lgaid.HasValue && Lgaid != Guid.Empty)
            {
                var result = await _candidateService.GetCandidateByLgaAsync(Lgaid.Value, PageNumber, pageSize);

                if (result != null && result.Success && result.Data != null)
                {
                    ViewModel.Items = result.Data;
                    ViewModel.TotalItems = result.TotalCount;

                    var selectedLga = LgaList.FirstOrDefault(m => m.Id == Lgaid.Value);
                    ViewBag.LgaSelectedName = selectedLga?.Name ?? "";
                }
                else
                {
                    ViewData["ErrorMessage"] = result?.Message ?? "An unknown error occurred while retrieving candidates.";
                }
            }

            return View(ViewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetCandidatebyParty(Guid? PartyId, int PageNumber = 1, int PageSize = 10)
        {
            if (PageNumber < 1) PageNumber = 1;
            if (PageSize < 1) PageSize = 10;

            var PartyList = await _context.Party.AsNoTracking().ToListAsync();
            ViewBag.Party = new SelectList(PartyList, "Id", "Name", PartyId);

            var ViewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = new List<CandidateViewModel>(),
                PageNumber = PageNumber,
                PageSize = PageSize,
                TotalItems = 0
            };

            if (PartyId.HasValue && PartyId.Value != Guid.Empty)
            {
                var result = await _candidateService.GetAllCandidateViaParty(PartyId.Value, PageNumber, PageSize);
                if (result != null && result.Items != null)
                {
                    ViewModel.Items = result.Items;
                    ViewModel.TotalItems = result.TotalItems;

                    var selectedParty = PartyList.FirstOrDefault(m => m.Id == PartyId.Value);
                    ViewBag.selectedName = selectedParty?.Name;
                    ViewBag.SelectedPartyId = PartyId.Value;
                }
                else
                {
                    ViewData["ErrorMessage"] = "No candidate records found or an error occurred fetching candidate data.";
                }
            }

            return View(ViewModel);
        }

        // ─────────────────────────────────────────────
        // Update & Cache Methods
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> UpdateCandidate(Guid id, CancellationToken cancellationToken)
        {
            var candidate = await _context.Candidate.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
            if (candidate == null)
            {
                TempData["ErrorMessage"] = "Candidate not found.";
                return RedirectToAction(nameof(AllCandidate));
            }

            var formModel = new UpdateCandidateViewModel
            {
                CandidateID = candidate.Id,
                Name = candidate.Name,
                Manifesto = candidate.Manifesto,
                PartyId = candidate.PartyId,
                PositonId = candidate.PositionId,
                StateId = candidate.StateId
            };

            await PopulateUpdateDropdownsAsync(candidate.PartyId, candidate.PositionId, candidate.StateId);

            return View(formModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCandidate(UpdateCandidateViewModel model, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            var result = await _candidateService.UpdateCandidateAsync(model, userId, cancellationToken);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message;
                await PopulateUpdateDropdownsAsync(model.PartyId, model.PositonId, model.StateId);
                return View(model);
            }

            // --- AUDIT LOGGING ---
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: userId,
                action: "Candidate Updated",
                details: $"Updated candidate profile ID: {model.CandidateID}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(AllCandidate), new { model.CandidateID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetCacheSoftDelete(Guid candidateId, int pageNumber = 1, int pageSize = 10)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            var result = await _candidateService.SoftDeleteCandidateAsync(candidateId, userId);

            if (result.Success)
            {
                _candidateService.ClearCandidateCache(pageNumber, pageSize);

                // --- AUDIT LOGGING ---
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                Guid tenantId = _tenantProvider.GetCurrentTenantId();

                await _auditLogService.LogActivityAsync(
                    userId: userId,
                    action: "Candidate Soft-Deleted & Cache Cleared",
                    details: $"Soft-deleted candidate ID: {candidateId} and flushed cache",
                    ipAddress: ipAddress,
                    tenantId: tenantId != Guid.Empty ? tenantId : null
                );

                TempData["SuccessMessage"] = result.Message;
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(AllCandidate), new { pageNumber, pageSize });
        }

        // ─────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────
        private async Task PopulateCreateDropdownsAsync(ElectionEvent election, Guid? selectedStateId)
        {
            var tenantId = election.TenantId;

            var parties = await _context.Party
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => (tenantId.HasValue && p.TenantId == tenantId) ||
                            (!p.TenantId.HasValue) ||
                            p.TenantId == Guid.Empty)
                .ToListAsync();

            ViewBag.Party = new SelectList(parties, "Id", "Name");

            var positions = await _context.Position
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.ElectionEventId == election.Id)
                .ToListAsync();

            ViewBag.PositionView = new SelectList(positions, "Id", "Name");

            var states = await _context.States
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToListAsync();

            ViewBag.StateView = new SelectList(states, "Id", "Name", selectedStateId);
            ViewBag.StatesList = new SelectList(states, "Id", "Name");

            if (selectedStateId.HasValue && selectedStateId.Value != Guid.Empty)
            {
                var lgas = await _context.Lgas
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(l => l.StateId == selectedStateId.Value)
                    .ToListAsync();

                ViewBag.Lga = new SelectList(lgas, "Id", "Name");
            }
            else
            {
                ViewBag.Lga = Enumerable.Empty<SelectListItem>();
            }

            ViewBag.ElectionTitle = election.Title;
            ViewBag.ElectionCategory = election.Category;

            var customFields = await _context.ElectionCustomFields
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(f => f.ElectionEventId == election.Id)
                .ToListAsync();

            ViewBag.CustomFields = customFields;
        }

        private async Task PopulateUpdateDropdownsAsync(Guid? electionEventId, Guid? selectedParty = null, Guid? selectedPosition = null, Guid? selectedState = null)
        {
            ViewBag.Parties = new SelectList(await _context.Party.AsNoTracking().ToListAsync(), "Id", "Name", selectedParty);

            var positions = await _context.Position
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.ElectionEventId == electionEventId)
                .ToListAsync();

            ViewBag.Positions = new SelectList(positions, "Id", "Name", selectedPosition);
            ViewBag.States = new SelectList(await _context.States.AsNoTracking().ToListAsync(), "Id", "Name", selectedState);
        }
    }
}