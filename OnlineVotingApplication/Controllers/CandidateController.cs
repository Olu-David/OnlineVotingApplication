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
using Perfolizer.Mathematics.Randomization;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;

namespace OnlineVotingApplication.Controllers
{
    // Updated controller-level access for administrative tiers
  
    public class CandidateController : Controller
    {
        private readonly AppDbContext _context;
        private readonly iCandidateService _candidateService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<CandidateController> _logger;
        private readonly IEmailService _emailService;
        private readonly IAuditLogService _auditLogService;
        private readonly ITenantProvider _tenantProvider;

        #region CandidateController
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
        #endregion

        #region Index
        // ─────────────────────────────────────────────
        // CANDIDATE DASHBOARD (INDEX) - TENANT AWARE
        // ─────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "Candidate, SuperAdmin, PlatformAdmin, Official")]
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
        #endregion

        #region ApplyAsCandidate (GET & POST)
        // ─────────────────────────────────────────────
        // GET: Apply as Candidate
        // ─────────────────────────────────────────────
        [HttpGet]
        [Authorize]              // ← overrides class-level roles; only requires login
        public async Task<IActionResult> ApplyAsCandidate(Guid electionEventId)
        {
            // 1. Manual role check – only Voters may access
            var voter = await _userManager.GetUserAsync(User);
            if (voter == null)
            {
                TempData["Error"] = "You must be logged in to apply as a candidate.";
                return RedirectToAction("Login", "AuthService");
            }

            if (!await _userManager.IsInRoleAsync(voter, "Voter"))
            {
                TempData["Error"] = "Only registered voters can apply as candidates.";
                return RedirectToAction("Index", "Home");
            }

            // 2. Fetch the election (ignore query filters so tenant-less elections work)
            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == electionEventId && !e.IsDeleted);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event not found.";
                return RedirectToAction("Index", "Home");
            }

            // 3. Fetch available positions for this election
            var positions = await _context.Position
                .IgnoreQueryFilters()
                .Where(p => p.ElectionEventId == electionEventId)
                .ToListAsync();

            ViewBag.ElectionTitle = election.Title;

            // 4. Build the view model
            var model = new CandidateApplicationViewModel
            {
                ElectionEventId = election.Id,
                TenantId = election.TenantId,
                PositionOptions = new SelectList(positions, "Id", "Name"),
                FullName = voter.FullName,
                CandidateEmail = voter.Email ?? string.Empty
            };

            return View(model);
        }

        // ─────────────────────────────────────────────
        // POST: Apply as Candidate
        // ─────────────────────────────────────────────
        [HttpPost]
        [Authorize]              // ← overrides class-level roles; only requires login
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> ApplyAsCandidate(CandidateApplicationViewModel model)
        {
            // ─── 1. Manual role check – only Voters may apply ───────────────
            var voter = await _userManager.GetUserAsync(User);
            if (voter == null)
            {
                TempData["Error"] = "You must be logged in to apply as a candidate.";
                return RedirectToAction("Login", "AuthService");
            }

            if (!await _userManager.IsInRoleAsync(voter, "Voter"))
            {
                TempData["Error"] = "Only registered voters can apply as candidates.";
                return RedirectToAction("Index", "Home");
            }

            // ─── 2. Verify the election exists ──────────────────────────────
            var electionEvent = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == model.ElectionEventId && !m.IsDeleted);

            if (electionEvent == null)
            {
                TempData["Error"] = "You cannot fill out this form without a valid election event.";
                return RedirectToAction("Index", "Voter");
            }

            // ─── 3. Rehydrate dropdown if validation fails ──────────────────
            if (!ModelState.IsValid)
            {
                ViewBag.ElectionTitle = electionEvent.Title;
                var positions = await _context.Position
                    .IgnoreQueryFilters()
                    .Where(p => p.ElectionEventId == model.ElectionEventId)
                    .ToListAsync();

                model.PositionOptions = new SelectList(positions, "Id", "Name", model.SelectedPositionId);
                return View(model);
            }

            // ─── 4. Duplicate check ─────────────────────────────────────────
            // Grab the email safely from the form model or fallback to the voter object
            var cleanEmail = (model.CandidateEmail ?? voter.Email ?? string.Empty).Trim().ToLower();

            if (string.IsNullOrEmpty(cleanEmail))
            {
                TempData["Error"] = "Email address is required to apply.";
                return RedirectToAction(nameof(ApplyAsCandidate), new { electionEventId = model.ElectionEventId });
            }

            bool alreadyInvited = await _context.candidateInvitations
      .IgnoreQueryFilters()
      .AnyAsync(a => a.ElectionEventId == model.ElectionEventId
                  && a.CandidateEmail.ToLower() == cleanEmail
                  && !a.IsUsed);   // ← block if an active invitation exists

            if (alreadyInvited)
            {
                TempData["Error"] = "You have already submitted an application for this election event.";
                return RedirectToAction(nameof(ApplyAsCandidate), new { electionEventId = model.ElectionEventId });
            }
           


            // ─── 5. Create the invitation ───────────────────────────────────
            var token = GenerateCode();

            var application = new CandidateInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = electionEvent.TenantId,     // ← from the DB, not the form
                ElectionEventId = model.ElectionEventId,
                PositionId = model.SelectedPositionId,
                CandidateName = model.FullName ?? voter.FullName ?? string.Empty,
                CandidateEmail = cleanEmail,
                IsUsed = false,
                IsSent=false,
                Token = token,
                CreatedAt = DateTime.UtcNow
            };

            _context.candidateInvitations.Add(application);
            await _context.SaveChangesAsync();

            // ─── 6. Audit log ───────────────────────────────────────────────
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid? tenantId = electionEvent.TenantId;

            await _auditLogService.LogActivityAsync(
                userId: voter.Id,
                action: "Candidate Application Submitted",
                details: $"Voter applied as candidate for election ID: {model.ElectionEventId}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["Success"] = "Your candidate application has been submitted successfully! Please wait for admin review.";
            return RedirectToAction("Details", "Voter", new {electionEvent.Id});
        }  
        #endregion

        #region GenerateCode
        private string GenerateCode()
        {
            string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            int length = 8; // length of your random string

            char[] result = new char[length];
            byte[] data = new byte[length];
            RandomNumberGenerator.Fill(data); // Fills array with secure random bytes

            for (int i = 0; i < length; i++)
            {
                result[i] = chars[data[i] % chars.Length];
            }

            string randomString = new string(result);
            return randomString; 
        }
        #endregion

        #region SendCandidateInvite
        [HttpPost]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StandardPolicy")]
        public async Task<IActionResult> SendCandidateInvite(SendCandidateInvitation model)
        {
            if (model.Id == Guid.Empty)
            {
                TempData["ErrorMessage"] = "Invalid invitation reference ID.";
                return RedirectToAction(nameof(GetUnsentCandidateApplications));
            }

            // 1️⃣ Quickly grab just the token and election event ID needed for Url.Action
            // We use IgnoreQueryFilters here too so it never fails to find the record reference
            var inviteRef = await _context.candidateInvitations
                .IgnoreQueryFilters()
                .Where(m => m.Id == model.Id)
                .Select(m => new { m.Token, m.ElectionEventId })
                .FirstOrDefaultAsync();

            if (inviteRef == null)
            {
                TempData["ErrorMessage"] = "Invitation record not found.";
                return RedirectToAction(nameof(GetUnsentCandidateApplications));
            }

            // 2️⃣ Generate the secure link using Url.Action
            model.SecureLink = Url.Action(
                action: "CreateCandidate",
                controller: "Candidate",
                values: new { token = inviteRef.Token, electionEventId = inviteRef.ElectionEventId },
                protocol: Request.Scheme
            );

            // 3️⃣ Hand off to your business logic service method which handles the full security, email dispatch, and database update
            var result = await _candidateService.SendCandidateInviteAsync(model);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(GetUnsentCandidateApplications));
            }

            TempData["SuccessMessage"] = result.Message ?? "Invitation sent successfully.";
            return RedirectToAction(nameof(GetSentCandidateInvitations));
        }
        #endregion


        #region PendingCandidateApplications
        [HttpGet("UnseNtCandidate-applications")]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
        public async Task<IActionResult> GetUnsentCandidateApplications(int pageNumber = 1, int pageSize = 10)
        {
            var paginatedResult = await _candidateService.GetUnsentCandidateApplicationsAsync(pageNumber, pageSize);
            return View(paginatedResult);
        }

        [HttpGet("SentCandidate-applications")]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
        public async Task<IActionResult> GetSentCandidateInvitations(int pageNumber = 1, int pageSize = 10)
        {
            var paginatedResult = await _candidateService.GetSentCandidateInvitationsAsync(pageNumber, pageSize);
            return View(paginatedResult);
        }
        #endregion

        #region CreateCandidate (GET)

        [HttpGet]
        [Authorize(Roles = "Voter, Candidate")]
        public async Task<IActionResult> CreateCandidate(Guid electionEventId, string token)
        {
            _logger.LogInformation("CreateCandidate GET → ElectionEventId={ElectionEventId}, Token={Token}",
                electionEventId, token);

            if (string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "A secure invitation link is required.";
                return RedirectToAction("Index", "Voter");
            }

            var trimmedToken = token.Trim();

            // 1. Load invitation by token
            var invitation = await _context.candidateInvitations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Token == trimmedToken);

            if (invitation == null)
            {
                _logger.LogWarning("GET: Invitation NOT FOUND for token '{Token}'", trimmedToken);
                TempData["ErrorMessage"] = "This registration link is invalid or does not exist.";
                return RedirectToAction("Index", "Voter");
            }

            if (invitation.IsUsed)
            {
                _logger.LogWarning("GET: Invitation {Id} already used", invitation.Id);
                TempData["ErrorMessage"] = "This registration link has already been used.";
                return RedirectToAction("Index", "Voter");
            }

            if (!invitation.IsSent)
            {
                _logger.LogWarning("GET: Invitation {Id} not marked as sent", invitation.Id);
                TempData["ErrorMessage"] = "This invitation has not been sent yet. Please contact support.";
                return RedirectToAction("Index", "Voter");
            }

            // 2. Load election
            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .Include(e => e.CustomFields)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == invitation.ElectionEventId && !e.IsDeleted);

            if (election == null)
            {
                TempData["ErrorMessage"] = "The linked election event could not be found.";
                return RedirectToAction("Index", "Voter");
            }

            // 3. Load position
            var position = await _context.Position
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == invitation.PositionId);

            if (position == null)
            {
                TempData["ErrorMessage"] = "The linked position could not be found. Please contact support.";
                return RedirectToAction("Index", "Voter");
            }

            // 4. Load user
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "AuthService");

            var fullName = string.IsNullOrWhiteSpace(user.FullName)
                ? (user.UserName ?? user.Email ?? "Candidate")
                : user.FullName;

            // 5. Build view model — bind Id + Token + locked display fields
            var vm = new CandidateViewModel
            {
                ElectionEventId = election.Id,
                PositionId = position.Id,
                CandidateInvitationID = invitation.Id,
                Name = fullName,
                LockedName = fullName,
                LockedPositionName = position.Name,
                LockedElectionTitle = election.Title,
                IsPolitical = election.Category == Enums.TenantCategory.Political
            };

            await PopulateCreateDropdownsAsync(vm, election, null);

            ViewBag.InviteToken = trimmedToken;

            return View(vm);
        }

        #endregion

        #region CreateCandidate (POST)

        [HttpPost]
        [Authorize(Roles = "Voter, Candidate")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StandardPolicy")]
        public async Task<IActionResult> CreateCandidate(CandidateViewModel model, string token)
        {
            // ═══ DEBUG BLOCK ═══
            _logger.LogWarning("POST CreateCandidate → Token RAW='{Token}', Trimmed='{Trimmed}', " +
                               "ModelInviteId={ModelId}, ElectionId={ElectionId}, PositionId={PositionId}",
                token, token?.Trim(), model?.CandidateInvitationID,
                model?.ElectionEventId, model?.PositionId);

            if (model == null || string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Invalid submission.";
                return RedirectToAction("Index", "Voter");
            }

            var trimmedToken = token.Trim();
            Guid currentTenantId = _tenantProvider.GetCurrentTenantId();

            // 1. Trusted user name
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Your session has expired. Please log in again.";
                return RedirectToAction("Login", "Account");
            }

            model.Name = string.IsNullOrWhiteSpace(user.FullName)
                ? (user.UserName ?? user.Email ?? "Candidate")
                : user.FullName;

            ModelState.Remove(nameof(model.Name));

            // 2. Load invitation by token
            var invitation = await _context.candidateInvitations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Token == trimmedToken);

            if (invitation == null)
            {
                _logger.LogWarning("POST: Invitation NOT FOUND for token '{Token}'", trimmedToken);
                TempData["ErrorMessage"] = "This registration link is invalid or does not exist.";
                return RedirectToAction("Index", "Voter");
            }

            if (invitation.IsUsed)
            {
                TempData["ErrorMessage"] = "This registration link has already been used.";
                return RedirectToAction("Index", "Voter");
            }

            if (!invitation.IsSent)
            {
                TempData["ErrorMessage"] = "This invitation has not been sent yet. Please contact support.";
                return RedirectToAction("Index", "Voter");
            }

            // 3. Lock fields from invitation
            model.ElectionEventId = invitation.ElectionEventId ?? Guid.Empty;
            model.PositionId = invitation.PositionId;
            model.CandidateInvitationID = invitation.Id;

            ModelState.Remove(nameof(model.ElectionEventId));
            ModelState.Remove(nameof(model.PositionId));
            ModelState.Remove(nameof(model.CandidateInvitationID));

            // 4. Load election
            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .Include(e => e.CustomFields)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == invitation.ElectionEventId && !e.IsDeleted);

            if (election == null)
            {
                TempData["ErrorMessage"] = "The linked election event could not be found.";
                return RedirectToAction("Index", "Voter");
            }

            // 5. Non-political cleanup
            if (election.Category != Enums.TenantCategory.Political)
            {
                ModelState.Remove(nameof(model.PartyId));
                ModelState.Remove(nameof(model.StateId));
                ModelState.Remove(nameof(model.LgaId));
                model.PartyId = null;
                model.StateId = null;
                model.LgaId = null;
            }

            // 6. ModelState check
            if (!ModelState.IsValid)
            {
                var errorDetails = ModelState
                    .Where(kv => kv.Value!.Errors.Count > 0)
                    .SelectMany(kv => kv.Value!.Errors.Select(e => $"{kv.Key}: {e.ErrorMessage}"))
                    .ToList();

                _logger.LogWarning("ModelState invalid:\n{Errors}", string.Join("\n", errorDetails));
                TempData["ErrorMessage"] = "Validation Errors: " + string.Join(" | ", errorDetails);

                // Rehydrate locked fields
                await RehydrateLockedFieldsAsync(model, invitation, election);
                await PopulateCreateDropdownsAsync(model, election, model.StateId);

                ViewBag.InviteToken = trimmedToken;
                return View(model);
            }

            // 7. Create candidate via service
            var result = await _candidateService.CreateCandidateAsync(model, user.Id, trimmedToken);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "An error occurred while saving the candidate.");
                TempData["ErrorMessage"] = result.Message;

                // Rehydrate locked fields
                await RehydrateLockedFieldsAsync(model, invitation, election);
                await PopulateCreateDropdownsAsync(model, election, model.StateId);

                ViewBag.InviteToken = trimmedToken;
                return View(model);
            }

            // 8. Audit log
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _auditLogService.LogActivityAsync(
                userId: user.Id,
                action: "Candidate Self-Registered",
                details: $"Candidate '{model.Name}' created for election {model.ElectionEventId}",
                ipAddress: ipAddress,
                tenantId: currentTenantId != Guid.Empty ? currentTenantId : null
            );

            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(AllCandidate));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Reloads locked display fields from invitation + election so the view
        /// doesn't lose them after a ModelState or service error.
        /// </summary>
        private async Task RehydrateLockedFieldsAsync(
            CandidateViewModel model,
            CandidateInvitation invitation,
            ElectionEvent election)
        {
            var position = await _context.Position
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == invitation.PositionId);

            var user = await _userManager.GetUserAsync(User);
            var fullName = user == null
                ? model.Name ?? "Candidate"
                : string.IsNullOrWhiteSpace(user.FullName)
                    ? (user.UserName ?? user.Email ?? "Candidate")
                    : user.FullName;

            model.LockedName = fullName;
            model.Name = fullName;
            model.LockedElectionTitle = election.Title;
            model.LockedPositionName = position?.Name ?? "Unknown Position";
            model.IsPolitical = election.Category == Enums.TenantCategory.Political;
            model.ElectionEventId = election.Id;
            model.PositionId = invitation.PositionId;
            model.CandidateInvitationID = invitation.Id;
        }

        private async Task PopulateCreateDropdownsAsync(CandidateViewModel vm, ElectionEvent election, Guid? selectedStateId)
        {
            var tenantId = election.TenantId;

            var parties = await _context.Party
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => !p.TenantId.HasValue
                         || p.TenantId == Guid.Empty
                         || p.TenantId == tenantId)
                .OrderBy(p => p.Name)
                .ToListAsync();

            vm.Parties = new SelectList(parties, "Id", "Name", vm.PartyId);

            var positions = await _context.Position
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.ElectionEventId == election.Id && !p.IsDeleted)
                .OrderBy(p => p.Name)
                .ToListAsync();

            vm.Positions = new SelectList(positions, "Id", "Name", vm.PositionId);

            var states = await _context.States
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();

            vm.States = new SelectList(states, "Id", "Name", selectedStateId);

            if (selectedStateId.HasValue && selectedStateId.Value != Guid.Empty)
            {
                var lgas = await _context.Lgas
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(l => l.StateId == selectedStateId.Value)
                    .OrderBy(l => l.Name)
                    .ToListAsync();

                vm.Lga = new SelectList(lgas, "Id", "Name", vm.LgaId);
            }
            else
            {
                vm.Lga = new List<SelectListItem>();
            }

            vm.LockedElectionTitle = election.Title;
            vm.IsPolitical = election.Category == Enums.TenantCategory.Political;

            vm.CustomFields = await _context.ElectionCustomFields
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(f => f.ElectionEventId == election.Id)
                .ToListAsync();
        }

        #endregion

        #region AJAX – Get LGAs by State

        [HttpGet]
        [AllowAnonymous]
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

        #endregion
    
   

        #region SoftDelete
// ─────────────────────────────────────────────
// Soft Delete & Restore
// ─────────────────────────────────────────────
[HttpGet]
        public IActionResult SoftDelete()
        {
            return View();
        }
        #endregion

        #region SoftDelete (2)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StandardPolicy")]
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
        #endregion

        #region RestoreCandidate
        [HttpGet]
        public IActionResult RestoreCandidate()
        {
            return View();
        }
        #endregion

        #region RestoreCandidate (2)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StandardPolicy")]
        public async Task<IActionResult> RestoreCandidate(Guid id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Home");

            var result = await _candidateService.RestoreCandidateDeleteAsync(id, userId);
            if (!result.Success)
            {
                return RedirectToAction(nameof(GetAllSoftdelete));
            }

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
        #endregion

        #region GetCandidateByPosition
        // ─────────────────────────────────────────────
        // Queries & Filters
        // ─────────────────────────────────────────────
        // ─────────────────────────────────────────────
        // Filter & Retrieval Endpoints
        // ─────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
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
        #endregion

        #region AllCandidate
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
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
        #endregion

        #region GetCandidateByState
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
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
        #endregion

        #region GetAllSoftdelete
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
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
        #endregion

        #region GetCandidateByLga
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
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
        #endregion

        #region GetCandidatebyParty
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin, Official")]
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
        #endregion

        #region UpdateCandidate
        // ─────────────────────────────────────────────
        // Update & Cache Methods
        // ─────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin")]
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
        #endregion

        #region UpdateCandidate (2)
        [HttpPost]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StandardPolicy")]
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
        #endregion

        #region ResetCacheSoftDelete
        [HttpPost]
        [Authorize(Roles = "SuperAdmin, PlatformAdmin")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StandardPolicy")]
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
        #endregion




        #region PopulateUpdateDropdownsAsync
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
        #endregion
    }
}
