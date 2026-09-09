using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [Authorize] // base: any authenticated user
    [EnableRateLimiting("StandardPolicy")]
    public class VoterController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IVoteService _voteService;
        private readonly IAuditLogService _auditLogService;
        private readonly ITenantProvider _tenantProvider;
        private readonly ILogger<VoterController> _logger;
        public VoterController(
            AppDbContext context,
            IVoteService voteService,
            IAuditLogService auditLogService,
            ITenantProvider tenantProvider,
            ILogger<VoterController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _voteService = voteService ?? throw new ArgumentNullException(nameof(voteService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ─── Helper: tenant check only for admin roles ─────────────────────────────
        private bool IsTenantAuthorized(Guid? tenantId)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Official") || User.IsInRole("Tenant"))
            {
                var activeTenant = _tenantProvider.GetCurrentTenantId();
                return tenantId == activeTenant;
            }
            // Voters, Candidates, Auditors bypass tenant checks
            return true;
        }

        // ─── VOTER-ONLY ACTIONS (require "Voter" role) ─────────────────────────────

        // GET: /Voter/Index – shows all elections (no tenant filter)
        [HttpGet]
        [Authorize(Roles = "Voter")]
        public async Task<IActionResult> Index(string? searchTerm,string? sortBy,int page = 1,int pageSize = 10,CancellationToken cancellationToken = default)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "Index";

            sortBy ??= "top";

            var response = await _voteService.GetElectionsForVoterAsync(
                searchTerm,
                sortBy,
                page,
                pageSize,
                cancellationToken);

            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
                return View(new PaginatedListViewModel<ElectionDto>());
            }

            ViewBag.CurrentSearch = searchTerm;
            ViewBag.CurrentSort = sortBy;

            return View(response.Data);
        }

        // GET: /Voter/Details/{id}
        [HttpGet]
        [Authorize(Roles = "Voter")]
        public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "Details";

            _logger.LogInformation("Details called with Id: {Id}", id);

            if (id == Guid.Empty)
            {
                TempData["ErrorMessage"] = "Invalid election ID.";
                return RedirectToAction(nameof(Index));
            }

            var election = await _context.ElectionEvents.IgnoreQueryFilters()
                .Include(e => e.Positions!)!
                    .ThenInclude(p => p.Candidates!)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

            if (election == null)
            {
                _logger.LogWarning("Election with Id {Id} not found in the database.", id);
                TempData["ErrorMessage"] = "Election not found. It may have been deleted or you may have followed an outdated link.";
                return RedirectToAction(nameof(Index));
            }

            // Check if user has already applied as candidate (for the "Apply" button)
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            bool hasApplied = false;
            if (!string.IsNullOrEmpty(userId))
            {
                hasApplied = await _context.Candidate
                    .AnyAsync(c => c.ElectionEventId == id && c.UserId == userId, cancellationToken);
            }
            ViewBag.HasApplied = hasApplied;

            return View(election);
        }
        // GET: /Voter/RequestCode
        [HttpGet]
        [Authorize(Roles = "Voter")]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> RequestCode(Guid electionId, Guid candidateId, Guid positionId, CancellationToken cancellationToken = default)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "RequestCode";

            string? voterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(voterId))
            {
                TempData["ErrorMessage"] = "User session expired.";
                return RedirectToAction("Index", "Home");
            }

            var response = await _voteService.GenerateAndQueueConfirmationCodeAsync(voterId, electionId);

            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
                return RedirectToAction("Details", new { id = electionId });
            }

            // Fetch candidate/position names for the view
            var candidate = await _context.Candidate
                .Where(c => c.Id == candidateId)
                .Select(c => new { c.Name })
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            var position = await _context.Position
                .Where(p => p.Id == positionId)
                .Select(p => new { p.Name })
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            ViewBag.ElectionId = electionId;
            ViewBag.CandidateId = candidateId;
            ViewBag.PositionId = positionId;
            ViewBag.CandidateName = candidate?.Name ?? "Selected Candidate";
            ViewBag.PositionName = position?.Name ?? "Selected Position";

            TempData["SuccessMessage"] = "A 6‑character confirmation code has been sent to your email.";
            return View();
        }

        // POST: /Voter/ConfirmAndVote
        [HttpPost]
        [Authorize(Roles = "Voter")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> ConfirmAndVote(Guid electionId, Guid candidateId, Guid positionId, string enteredCode, CancellationToken cancellationToken = default)
        {
            string? voterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(voterId))
            {
                TempData["ErrorMessage"] = "User session expired.";
                return RedirectToAction("Index", "Home");
            }

            var response = await _voteService.ConfirmAndCastVoteAsync(voterId, electionId, enteredCode, candidateId, positionId);

            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
                return RedirectToAction("RequestCode", new { electionId, candidateId, positionId });
            }

            // Audit logging
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: voterId,
                action: "Vote Cast",
                details: $"Voted for candidate ID: {candidateId} in election ID: {electionId}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = response.Data ?? "Your vote has been successfully submitted!";
            return RedirectToAction("ConfirmationSuccess");
        }

        // GET: /Voter/ConfirmationSuccess
        [HttpGet]
        [Authorize(Roles = "Voter")]
        public IActionResult ConfirmationSuccess()
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "ConfirmationSuccess";
            return View();
        }

        // GET: /Voter/MyHistory
        [HttpGet]
        [Authorize(Roles = "Voter")]
        public async Task<IActionResult> MyHistory(CancellationToken cancellationToken = default)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "MyHistory";

            string? voterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(voterId))
            {
                TempData["ErrorMessage"] = "User session expired.";
                return RedirectToAction("Index", "Home");
            }

            var response = await _voteService.GetElectionsTakenByVoterAsync(voterId);

            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
                return View(new List<ElectionEvent>());
            }

            return View(response.Data);
        }

        // ─── SHARED / MANAGEMENT ACCESSIBLE ACTIONS ────────────────────────────────

        // GET: /Voter/LiveResults – any authenticated user can view if they've voted or are management
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> LiveResults(Guid electionEventId, CancellationToken cancellationToken = default)
        {
            var userEmail = User.Identity?.Name?.Trim().ToLower();
            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["Error"] = "You must be logged in to view live results.";
                return RedirectToAction("Index", "Home");
            }

            // Check if user has voted in this election
            bool hasVoted = await _context.Votes
                .AnyAsync(v => v.ElectionId == electionEventId
                            && v.Voter != null
                            && v.Voter.Email!.ToLower() == userEmail,
                          cancellationToken);

            bool isManagement = User.IsInRole("SuperAdmin") || User.IsInRole("PlatformAdmin") || User.IsInRole("Official");
            bool canViewResults = hasVoted || isManagement;

            List<CandidateVoteDto> voteData = new();
            if (canViewResults)
            {
                var resultResponse = await _voteService.GetElectionResultsAsync(electionEventId);
                if (resultResponse.Success)
                    voteData = resultResponse.Data ?? new List<CandidateVoteDto>();
                else
                    TempData["Error"] = "Could not retrieve results.";
            }

            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "LiveResults";
            ViewData["ElectionEventId"] = electionEventId;
            ViewData["CanViewResults"] = canViewResults;
            return View(voteData);
        }

        // ─── ADMIN-ONLY ACTIONS ─────────────────────────────────────────────────────

        // GET: /Voter/PenalizedVoters
        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Official")]
        public async Task<IActionResult> PenalizedVoters(CancellationToken cancellationToken = default)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "PenalizedVoters";

            var penalizedList = await _context.Votes
                .Where(v => v.IsPenalized)
                .Include(v => v.Voter)
                .Include(v => v.Election)
                .Include(v => v.Candidate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return View(penalizedList);
        }

        // GET: /Voter/BallotBreakdown
        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Official,Candidate")]
        public async Task<IActionResult> BallotBreakdown(Guid electionId, CancellationToken cancellationToken = default)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "BallotBreakdown";

            string? voterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(voterId))
            {
                TempData["ErrorMessage"] = "User session expired.";
                return RedirectToAction("Index", "Home");
            }

            var response = await _voteService.GetVoterBallotHistoryAsync(voterId, electionId);
            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
                return RedirectToAction(nameof(MyHistory));
            }

            return View(response.Data);
        }

        // GET: /Voter/ManualEntry
        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Official,Tenant")]
        public async Task<IActionResult> ManualEntry(Guid? electionId, Guid? positionId, CancellationToken cancellationToken = default)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "ManualEntry";

            ViewBag.Elections = await _context.ElectionEvents
                .Where(e => !e.IsDeleted)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            ViewBag.Positions = await _context.Position
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var model = new ManualResultViewModel();

            if (electionId.HasValue && positionId.HasValue)
            {
                // Admin only – enforce tenant check
                var election = await _context.ElectionEvents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == electionId.Value, cancellationToken);
                if (election == null || !IsTenantAuthorized(election.TenantId))
                {
                    TempData["ErrorMessage"] = "Unauthorized tenant access.";
                    return RedirectToAction(nameof(Index));
                }

                model.ElectionEventId = electionId.Value;
                model.PositionId = positionId.Value;

                var candidates = await _context.Candidate
                    .Where(c => c.ElectionEventId == electionId && c.PositionId == positionId && !c.isDeleted)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                model.CandidateVotes = candidates.Select(c => new CandidateVoteInput
                {
                    CandidateId = c.Id,
                    CandidateName = c.Name ?? "Unnamed Candidate",
                    ManualVoteCount = 0
                }).ToList();
            }

            return View(model);
        }

        // POST: /Voter/ManualEntry
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,Official,Tenant")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> ManualEntry(ManualResultViewModel model, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Ctrl"] = "Voter";
                ViewData["Action"] = "ManualEntry";
                ViewBag.Elections = await _context.ElectionEvents.Where(e => !e.IsDeleted).AsNoTracking().ToListAsync(cancellationToken);
                ViewBag.Positions = await _context.Position.AsNoTracking().ToListAsync(cancellationToken);
                return View(model);
            }

            // Admin only – enforce tenant check
            var election = await _context.ElectionEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == model.ElectionEventId, cancellationToken);
            if (election == null || !IsTenantAuthorized(election.TenantId))
            {
                TempData["ErrorMessage"] = "Unauthorized tenant access.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var entry in model.CandidateVotes ?? new List<CandidateVoteInput>())
            {
                if (entry.ManualVoteCount > 0)
                {
                    for (int i = 0; i < entry.ManualVoteCount; i++)
                    {
                        var vote = new Vote
                        {
                            Id = Guid.NewGuid(),
                            CandidateId = entry.CandidateId,
                            ElectionId = model.ElectionEventId,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.Votes.Add(vote);
                    }
                }
                else if (entry.ManualVoteCount < 0)
                {
                    int removeCount = Math.Abs(entry.ManualVoteCount);
                    var existingVotes = await _context.Votes
                        .Where(v => v.CandidateId == entry.CandidateId && v.ElectionId == model.ElectionEventId)
                        .OrderByDescending(v => v.CreatedAt)
                        .Take(removeCount)
                        .ToListAsync(cancellationToken);

                    if (existingVotes.Any())
                        _context.Votes.RemoveRange(existingVotes);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            string adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            await _auditLogService.LogActivityAsync(
                userId: adminId,
                action: "Manual Results Updated",
                details: $"Manually adjusted votes for election ID: {model.ElectionEventId}",
                ipAddress: ipAddress,
                tenantId: tenantId != Guid.Empty ? tenantId : null
            );

            TempData["SuccessMessage"] = "Manual votes successfully updated (added/subtracted)!";
            return RedirectToAction("LiveResults");
        }

        // POST: /Voter/PenalizeVoter
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,Official")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> PenalizeVoter(string voterId, Guid electionId, string reason, Guid returnElectionId, CancellationToken cancellationToken = default)
        {
            string? adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminId))
            {
                TempData["ErrorMessage"] = "User session expired.";
                return RedirectToAction("Index", "Home");
            }

            var response = await _voteService.PenalizeVoterAsync(voterId, electionId, reason, adminId);

            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
            }
            else
            {
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                Guid tenantId = _tenantProvider.GetCurrentTenantId();

                await _auditLogService.LogActivityAsync(
                    userId: adminId,
                    action: "Voter Penalized",
                    details: $"Penalized voter ID: {voterId} for election ID: {electionId}. Reason: {reason}",
                    ipAddress: ipAddress,
                    tenantId: tenantId != Guid.Empty ? tenantId : null
                );

                TempData["SuccessMessage"] = response.Data;
            }

            return RedirectToAction("Details", new { id = returnElectionId });
        }
    }
}