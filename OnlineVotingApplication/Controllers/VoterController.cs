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
    [Authorize]
    [EnableRateLimiting("StandardPolicy")]
    public class VoterController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IVoteService _voteService;
        private readonly IAuditLogService _auditLogService;
        private readonly ITenantProvider _tenantProvider;

        public VoterController(
            AppDbContext context,
            IVoteService voteService,
            IAuditLogService auditLogService,
            ITenantProvider tenantProvider)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _voteService = voteService ?? throw new ArgumentNullException(nameof(voteService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        }

        // GET: /Voter/Index (Lists election events by tenant)
        [HttpGet]
        public async Task<IActionResult> Index(Guid tenantId)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "Index";

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (tenantId == Guid.Empty)
            {
                tenantId = activeTenantId;
            }

            if (!isSuperAdmin && tenantId != activeTenantId)
            {
                TempData["ErrorMessage"] = "Unauthorized tenant access.";
                return RedirectToAction("Index", "Home");
            }

            var elections = await _context.ElectionEvents
                .Where(e => e.TenantId == tenantId && !e.IsDeleted)
                .ToListAsync();

            ViewBag.TenantId = tenantId;
            return View(elections);
        }

        // GET: /Voter/PenalizedVoters (Lists all penalized voters across elections)
        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Official")]
        public async Task<IActionResult> PenalizedVoters()
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "PenalizedVoters";

            var penalizedList = await _context.Votes
                .Where(v => v.IsPenalized)
                .Include(v => v.Voter)
                .Include(v => v.Election)
                .Include(v => v.Candidate)
                .ToListAsync();

            return View(penalizedList);
        }

        // GET: /Voter/Details/5 (Shows election details, positions, and candidates)
        [HttpGet]
        public async Task<IActionResult> Details(Guid id) // id = ElectionId
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "Details";

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var election = await _context.ElectionEvents
                .Include(e => e.Positions!)!
                    .ThenInclude(p => p.Candidates!)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (election == null)
            {
                TempData["ErrorMessage"] = "Election event not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin && election.TenantId != activeTenantId)
            {
                TempData["ErrorMessage"] = "Unauthorized access to election details.";
                return RedirectToAction(nameof(Index), new { tenantId = activeTenantId });
            }

            return View(election);
        }

        // GET: /Voter/RequestCode?electionId=xxx&candidateId=yyy&positionId=zzz
        [HttpGet]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> RequestCode(Guid electionId, Guid candidateId, Guid positionId)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "RequestCode";

            string voterId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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

            ViewBag.ElectionId = electionId;
            ViewBag.CandidateId = candidateId;
            ViewBag.PositionId = positionId;
            TempData["SuccessMessage"] = "A 6-character confirmation code has been sent to your email.";

            return View();
        }

        // POST: /Voter/ConfirmAndVote
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> ConfirmAndVote(Guid electionId, Guid candidateId, Guid positionId, string enteredCode)
        {
            string voterId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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

            // --- AUDIT LOGGING ---
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

        [HttpGet]
        public IActionResult ConfirmationSuccess()
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "ConfirmationSuccess";
            return View();
        }

        // GET: /Voter/MyHistory
        [HttpGet]
        public async Task<IActionResult> MyHistory()
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "MyHistory";

            string voterId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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

        // GET: /Voter/BallotBreakdown?electionId=xxx
        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Official,Candidate")]
        public async Task<IActionResult> BallotBreakdown(Guid electionId)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "BallotBreakdown";

            string voterId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var response = await _voteService.GetVoterBallotHistoryAsync(voterId, electionId);

            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
                return RedirectToAction(nameof(MyHistory));
            }

            return View(response.Data);
        }

        [HttpGet]
        public async Task<IActionResult> LiveResults()
        {
            var voteData = await _context.Candidate
                .Select(c => new CandidateVoteDto
                {
                    CandidateName = c.Name ?? "",
                    VoteCount = _context.Votes.Count(v => v.CandidateId == c.Id)
                })
                .ToListAsync();

            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "LiveResults";

            return View(voteData);
        }

        // GET: /Voter/ManualEntry
        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Official,Tenant")]
        public async Task<IActionResult> ManualEntry(Guid? electionId, Guid? positionId)
        {
            ViewData["Ctrl"] = "Voter";
            ViewData["Action"] = "ManualEntry";

            ViewBag.Elections = await _context.ElectionEvents
                .Where(e => !e.IsDeleted)
                .ToListAsync();

            ViewBag.Positions = await _context.Position.ToListAsync();

            var model = new ManualResultViewModel();

            if (electionId.HasValue && positionId.HasValue)
            {
                model.ElectionEventId = electionId.Value;
                model.PositionId = positionId.Value;

                var candidates = await _context.Candidate
                    .Where(c => c.ElectionEventId == electionId && c.PositionId == positionId && !c.isDeleted)
                    .ToListAsync();

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
        public async Task<IActionResult> ManualEntry(ManualResultViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Ctrl"] = "Voter";
                ViewData["Action"] = "ManualEntry";
                ViewBag.Elections = await _context.ElectionEvents.Where(e => !e.IsDeleted).ToListAsync();
                ViewBag.Positions = await _context.Position.ToListAsync();
                return View(model);
            }

            foreach (var entry in model.CandidateVotes)
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
                        .ToListAsync();

                    if (existingVotes.Any())
                    {
                        _context.Votes.RemoveRange(existingVotes);
                    }
                }
            }

            await _context.SaveChangesAsync();

            // --- AUDIT LOGGING ---
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
        public async Task<IActionResult> PenalizeVoter(string voterId, Guid electionId, string reason, Guid returnElectionId)
        {
            string adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var response = await _voteService.PenalizeVoterAsync(voterId, electionId, reason, adminId);

            if (!response.Success)
            {
                TempData["ErrorMessage"] = response.Message;
            }
            else
            {
                // --- AUDIT LOGGING ---
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