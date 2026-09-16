using Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Jobs;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Services;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace OnlineVotingApplication.Repository.Services
{
    public class VoteService : IVoteService
    {
        private readonly AppDbContext _context;
        private readonly IDistributedCache _cache;
        private readonly ILogger<VoteService> _logger;
        private readonly IEmailService _emailService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly VotingChannel _channel;

        #region VoteService
        public VoteService(AppDbContext context, IDistributedCache cache, ILogger<VoteService> logger, UserManager<ApplicationUser> userManager, IEmailService emailService, VotingChannel channel)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
            _userManager = userManager;
            _emailService = emailService;
            _channel = channel;
        }
        #endregion

        #region GetElectionsForVoterAsync
        public async Task<ServiceResponse<PaginatedListViewModel<ElectionDto>>> GetElectionsForVoterAsync(string? searchTerm,string? sortBy,int pageNumber,int pageSize,CancellationToken cancellationToken = default)
        {
            try
            {
                // Build cache key
                var cacheKey = $"voter_elec_{searchTerm?.Trim()?.ToLower() ?? "all"}_{sortBy ?? "top"}_p{pageNumber}_s{pageSize}";

                // Try to get from cache
                var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
                if (!string.IsNullOrEmpty(cached))
                {
                    var cachedResult = JsonSerializer.Deserialize<PaginatedListViewModel<ElectionDto>>(cached);
                    if (cachedResult != null)
                        return new ServiceResponse<PaginatedListViewModel<ElectionDto>> { Success = true, Data = cachedResult };
                }

                // Build query
                var query = _context.ElectionEvents.IgnoreQueryFilters()
                    .Where(e => !e.IsDeleted)
                    .AsNoTracking();

                // Search filter
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var term = searchTerm.Trim().ToLower();
                    query = query.Where(e =>
                        (e.Title != null && e.Title.ToLower().Contains(term)) ||
                        (e.Description != null && e.Description.ToLower().Contains(term))
                    );
                }

                // Sorting
                query = sortBy?.ToLower() switch
                {
                    "recent" => query.OrderByDescending(e => e.CreatedAt),   
                    "closing" => query.OrderBy(e => e.EndDate),
                    _ => query.OrderByDescending(e => e.StartDate)         
                };

                var totalCount = await query.CountAsync(cancellationToken);

                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(e => new ElectionDto
                    {
                        Id = e.Id,
                        Title = e.Title ?? string.Empty,
                        Description = e.Description ?? string.Empty,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate,
                        ImageUrl = e.ImageUrl,
                        TenantId = e.TenantId
                    })
                    .ToListAsync(cancellationToken);

                var result = new PaginatedListViewModel<ElectionDto>
                {
                    Items = items,
                    TotalItems = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                // Cache for 3 minutes
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
                };
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), cacheOptions, cancellationToken);

                return new ServiceResponse<PaginatedListViewModel<ElectionDto>>
                {
                    Success = true,
                    Data = result,
                    Message = "Elections retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving paginated elections for voters.");
                return new ServiceResponse<PaginatedListViewModel<ElectionDto>>
                {
                    Success = false,
                    Message = "Failed to retrieve elections."
                };
            }
        }
        #endregion

        #region GetResultsAsync
        public async Task<ServiceResponse<List<VoteResultDto>>> GetResultsAsync(Guid electionId, Guid positionId)
        {
            try
            {
                var results = await _context.Votes
                    .Where(v => v.ElectionId == electionId && v.PositionId == positionId && v.IsConfirmed && !v.IsPenalized)
                    .GroupBy(v => v.CandidateId)
                    .Select(g => new VoteResultDto
                    {
                        CandidateId = g.Key,
                        VoteCount = g.Count()
                    })
                    .ToListAsync();

                return new ServiceResponse<List<VoteResultDto>>
                {
                    Success = true,
                    Data = results,
                    Message = "Results retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching results for election {ElectionId}, position {PositionId}", electionId, positionId);
                return new ServiceResponse<List<VoteResultDto>> { Success = false, Message = "Failed to retrieve results." };
            }
        }
        #endregion

        #region GetVoteByStateViaPosition
        public async Task<ServiceResponse<List<StateResultDto>>> GetVoteByStateViaPosition(Guid electionID, Guid positionID)
        {
            try
            {
                var data = await _context.Votes
                    .Where(v => v.ElectionId == electionID && v.PositionId == positionID && v.IsConfirmed && !v.IsPenalized)
                    .Join(_context.Users, v => v.VoterId, u => u.Id, (v, u) => new
                    {
                        StateId = u.State != null ? u.State.Id : (Guid?)null,
                        StateName = u.State != null ? u.State.Name : "Unknown",
                        v.CandidateId
                    })
                    .GroupBy(x => new { x.StateId, x.StateName, x.CandidateId })
                    .Select(g => new StateResultDto
                    {
                        StateId = g.Key.StateId ?? Guid.Empty,
                        State = g.Key.StateId ?? Guid.Empty,
                        StateName = g.Key.StateName,
                        CandidateId = g.Key.CandidateId,
                        VoteCount = g.Count()
                    })
                    .ToListAsync();

                return new ServiceResponse<List<StateResultDto>>
                {
                    Success = true,
                    Data = data,
                    Message = "State results retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching state results for election {ElectionId}", electionID);
                return new ServiceResponse<List<StateResultDto>> { Success = false, Message = "Failed to retrieve state results." };
            }
        }
        #endregion

        #region GenerateAndQueueConfirmationCodeAsync
        public async Task<ServiceResponse<string>> GenerateAndQueueConfirmationCodeAsync(
            string voterId,
            Guid electionId,
            Guid positionId)
        {
            try
            {
                // ── 1. Load or create the vote session ───────────────────
                var voteRecord = await _context.Votes
                    .FirstOrDefaultAsync(v => v.VoterId == voterId
                                           && v.ElectionId == electionId
                                           && v.PositionId == positionId);

                if (voteRecord == null)
                {
                    voteRecord = new Vote
                    {
                        Id = Guid.NewGuid(),
                        VoterId = voterId,
                        ElectionId = electionId,
                        PositionId = positionId,
                        IsConfirmed = false,
                        HasVoted = false,
                        IsPenalized = false,
                        FailedAttempts = 0
                    };
                    _context.Votes.Add(voteRecord);
                }

                // ── 2. Guard: already voted or penalized ─────────────────
                if (voteRecord.HasVoted)
                {
                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = "You have already cast your ballot for this position."
                    };
                }

                if (voteRecord.IsPenalized)
                {
                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = "Your voting session has been penalized. Please contact support."
                    };
                }

                // ── 3. Guard: existing code still valid? ─────────────────
                //    If a code exists AND hasn't expired, don't issue a new one.
                bool codeStillValid =
                    !string.IsNullOrEmpty(voteRecord.ConfirmationCode) &&
                    voteRecord.ConfirmationCodeExpiry.HasValue &&
                    voteRecord.ConfirmationCodeExpiry.Value > DateTime.UtcNow;

                if (codeStillValid)
                {
                    _logger.LogInformation(
                        "Voter {VoterId} already has a valid code for Position {PositionId}. Skipping regeneration.",
                        voterId, positionId);

                    return new ServiceResponse<string>
                    {
                        Success = true,
                        Data = "Code already active.",
                        Message = "A confirmation code is already active. Please check your email. " +
                                  "You can request a new one after it expires."
                    };
                }

                // ── 4. Generate a fresh code + new expiry ────────────────
                string code = GenerateSecureConfirmationCode();

                voteRecord.ConfirmationCode = code;
                voteRecord.ConfirmationCodeExpiry = DateTime.UtcNow.AddMinutes(10);
                voteRecord.FailedAttempts = 0;   // reset attempts on new code
                voteRecord.IsConfirmed = false;  // fresh session state

                await _context.SaveChangesAsync();

                // ── 5. Look up voter + election for the email ────────────
                var voter = await _userManager.FindByIdAsync(voterId);
                if (voter == null || string.IsNullOrEmpty(voter.Email))
                {
                    _logger.LogWarning("Voter {VoterId} has no email on file.", voterId);
                    return new ServiceResponse<string>
                    {
                        Success = true,
                        Data = code,
                        Message = "Confirmation code generated, but no email on file."
                    };
                }

                var electionTitle = await _context.ElectionEvents
                    .Where(e => e.Id == electionId)
                    .Select(e => e.Title)
                    .FirstOrDefaultAsync() ?? "the upcoming election";

                string recipientName = !string.IsNullOrWhiteSpace(voter.FullName)
                    ? voter.FullName
                    : (voter.UserName ?? "Voter");

                // ── 6. Send the styled confirmation email ────────────────
                string subject = $"Your VoteX Confirmation Code — {electionTitle}";
                string htmlBody = BuildConfirmationEmail(recipientName, code, electionTitle);

                await _emailService.EmailSendAsync(voter.Email, subject, htmlBody);

                _logger.LogInformation(
                    "Confirmation code {Code} sent to {Email} for Position {PositionId}",
                    code, voter.Email, positionId);

                return new ServiceResponse<string>
                {
                    Success = true,
                    Data = code,
                    Message = "Confirmation code generated and emailed successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate confirmation code for Voter {VoterId}", voterId);
                return new ServiceResponse<string>
                {
                    Success = false,
                    Message = "Failed to generate confirmation code. Please try again."
                };
            }
        }
        #endregion

        #region BuildConfirmationEmail
        private static string BuildConfirmationEmail(string recipientName, string code, string electionTitle)
        {
            const string navy = "#0f1a38";
            const string amber = "#f5a623";
            const string amberDark = "#d4891a";
            const string paper = "#f7f5f0";
            const string textMuted = "#6b7280";

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>VoteX Confirmation Code</title>
</head>
<body style='margin:0;padding:0;background-color:#eef1f6;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Helvetica,Arial,sans-serif;'>

    <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'
           style='background-color:#eef1f6;padding:40px 16px;'>
        <tr>
            <td align='center'>

                <table role='presentation' width='600' cellpadding='0' cellspacing='0' border='0'
                       style='max-width:600px;width:100%;background:#ffffff;border-radius:14px;overflow:hidden;
                              box-shadow:0 12px 32px rgba(15,26,56,0.08);'>

                    <!-- HEADER -->
                    <tr>
                        <td style='background:{navy};padding:36px 40px 28px;text-align:center;'>
                            <table role='presentation' cellpadding='0' cellspacing='0' border='0' align='center'>
                                <tr>
                                    <td style='background:{amber};width:52px;height:52px;border-radius:14px;
                                               text-align:center;vertical-align:middle;'>
                                        <span style='font-size:26px;line-height:52px;color:{navy};'>&#128499;</span>
                                    </td>
                                </tr>
                            </table>
                            <h1 style='color:#ffffff;font-size:24px;font-weight:700;margin:16px 0 4px;
                                       letter-spacing:-0.3px;'>VoteX</h1>
                            <p style='color:rgba(255,255,255,0.7);font-size:13px;margin:0;
                                      letter-spacing:1.5px;text-transform:uppercase;'>
                                Secure Digital Democracy
                            </p>
                        </td>
                    </tr>

                    <!-- BODY -->
                    <tr>
                        <td style='padding:40px;'>
                            <h2 style='color:{navy};font-size:20px;font-weight:700;margin:0 0 16px;'>
                                Hi {recipientName},
                            </h2>

                            <p style='color:#334155;font-size:15px;line-height:1.6;margin:0 0 24px;'>
                                We received a request to cast a ballot in the
                                <strong style='color:{navy};'>{electionTitle}</strong>.
                                Use the confirmation code below to complete your vote.
                            </p>

                            <!-- CODE BOX -->
                            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'
                                   style='margin:0 0 28px;'>
                                <tr>
                                    <td align='center'
                                        style='background:{paper};border:2px dashed {amber};
                                               border-radius:12px;padding:28px 20px;'>
                                        <p style='color:{textMuted};font-size:11px;font-weight:600;
                                                  letter-spacing:2px;text-transform:uppercase;
                                                  margin:0 0 12px;'>Your Confirmation Code</p>
                                        <div style='font-family:""IBM Plex Mono"",""Courier New"",monospace;
                                                    font-size:36px;font-weight:700;letter-spacing:10px;
                                                    color:{navy};margin:0;line-height:1.2;'>
                                            {code}
                                        </div>
                                        <p style='color:{textMuted};font-size:12px;margin:12px 0 0;'>
                                            Expires in 10 minutes
                                        </p>
                                    </td>
                                </tr>
                            </table>

                            <!-- STEPS -->
                            <p style='color:{navy};font-size:14px;font-weight:700;margin:0 0 12px;'>
                                Next steps:
                            </p>
                            <table role='presentation' cellpadding='0' cellspacing='0' border='0'
                                   style='margin:0 0 28px;'>
                                <tr>
                                    <td style='padding:0 0 10px 0;color:#334155;font-size:14px;line-height:1.5;'>
                                        <span style='color:{amber};font-weight:700;margin-right:6px;'>1.</span>
                                        Return to the VoteX ballot page
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:0 0 10px 0;color:#334155;font-size:14px;line-height:1.5;'>
                                        <span style='color:{amber};font-weight:700;margin-right:6px;'>2.</span>
                                        Enter the code above
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:0;color:#334155;font-size:14px;line-height:1.5;'>
                                        <span style='color:{amber};font-weight:700;margin-right:6px;'>3.</span>
                                        Confirm your vote
                                    </td>
                                </tr>
                            </table>

                            <!-- WARNING -->
                            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'
                                   style='background:#fff7ed;border-left:4px solid {amber};
                                          border-radius:6px;margin:0 0 8px;'>
                                <tr>
                                    <td style='padding:16px 18px;'>
                                        <p style='color:#92400e;font-size:13px;font-weight:600;margin:0 0 4px;'>
                                            &#9888;&#65039; Never share this code
                                        </p>
                                        <p style='color:#78350f;font-size:12px;line-height:1.5;margin:0;'>
                                            VoteX staff will never ask for your confirmation code.
                                            If you did not request this, you can safely ignore this email.
                                        </p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- FOOTER -->
                    <tr>
                        <td style='padding:0 40px;'>
                            <div style='height:1px;background:#e2e8f0;'></div>
                        </td>
                    </tr>
                    <tr>
                        <td style='padding:28px 40px 36px;text-align:center;'>
                            <p style='color:{navy};font-size:13px;font-weight:700;margin:0 0 6px;'>VoteX</p>
                            <p style='color:{textMuted};font-size:12px;line-height:1.6;margin:0 0 12px;'>
                                Secure, transparent, real-time digital voting
                            </p>
                            <p style='color:{textMuted};font-size:11px;margin:0;'>
                                &copy; {DateTime.UtcNow.Year} VoteX. All rights reserved.
                            </p>
                        </td>
                    </tr>

                </table>

                <p style='color:{textMuted};font-size:11px;margin:20px 0 0;max-width:600px;
                          line-height:1.5;text-align:center;'>
                    Having trouble? Contact support at
                    <a href='mailto:support@votex.ng' style='color:{amberDark};
                       text-decoration:none;font-weight:600;'>support@votex.ng</a>
                </p>

            </td>
        </tr>
    </table>

</body>
</html>";
        }
        #endregion


        #region ConfirmAndCastVoteAsync
        public async Task<ServiceResponse<string>> ConfirmAndCastVoteAsync(
            string voterId,
            Guid electionId,
            string enteredCode,
            Guid candidateId,
            Guid positionId)
        {
            try
            {
                // ── 1. Load the vote session ─────────────────────────────
                var voteRecord = await _context.Votes
                    .FirstOrDefaultAsync(v => v.VoterId == voterId
                                           && v.ElectionId == electionId
                                           && v.PositionId == positionId);

                if (voteRecord == null)
                {
                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = "Invalid vote session. Please request a new confirmation code."
                    };
                }

                // ── 2. Penalty check ─────────────────────────────────────
                if (voteRecord.IsPenalized)
                {
                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = "Your voting session has been penalized. Please contact support."
                    };
                }

                // ── 3. No active code? ───────────────────────────────────
                if (string.IsNullOrEmpty(voteRecord.ConfirmationCode) ||
                    voteRecord.ConfirmationCodeExpiry == null)
                {
                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = "No active confirmation code. Please request a new one."
                    };
                }

                // ── 4. Expiry check ──────────────────────────────────────
                if (voteRecord.ConfirmationCodeExpiry.Value < DateTime.UtcNow)
                {
                    // Clear the expired code so the next request generates a new one
                    voteRecord.ConfirmationCode = null;
                    voteRecord.ConfirmationCodeExpiry = null;
                    await _context.SaveChangesAsync();

                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = "Your confirmation code has expired. Please request a new one."
                    };
                }

                // ── 5. Code match check ──────────────────────────────────
                if (!string.Equals(voteRecord.ConfirmationCode, enteredCode, StringComparison.Ordinal))
                {
                    voteRecord.FailedAttempts++;

                    _logger.LogWarning(
                        "Invalid code for Voter {VoterId}. Attempts: {Attempts}",
                        voterId, voteRecord.FailedAttempts);

                    if (voteRecord.FailedAttempts >= 5)
                    {
                        voteRecord.IsPenalized = true;

                        // Clear the code so it can't be retried even if timing permits
                        voteRecord.ConfirmationCode = null;
                        voteRecord.ConfirmationCodeExpiry = null;
                    }

                    await _context.SaveChangesAsync();

                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = voteRecord.IsPenalized
                            ? "Too many failed attempts. Your session has been penalized. Please contact support."
                            : $"Invalid confirmation code. {5 - voteRecord.FailedAttempts} attempt(s) remaining."
                    };
                }

                // ── 6. Already voted? ────────────────────────────────────
                if (voteRecord.HasVoted)
                {
                    return new ServiceResponse<string>
                    {
                        Success = false,
                        Message = "You have already cast your ballot for this position."
                    };
                }

                // ── 7. Mark as confirmed (queued) ────────────────────────
                voteRecord.IsConfirmed = true;
                voteRecord.CandidateId = candidateId;
                voteRecord.ConfirmationCode = null;               // consume the code
                voteRecord.ConfirmationCodeExpiry = null;
                voteRecord.FailedAttempts = 0;

                await _context.SaveChangesAsync();

                // ── 8. Push to channel for background processing ─────────
                var job = new VoteJob(
                    VoterId: voterId,
                    ElectionId: electionId,
                    CandidateId: candidateId,
                    PositionId: positionId,
                    StateId: voteRecord.StateId,
                    TenantId: voteRecord.TenantId,
                    ConfirmationCode: enteredCode
                );

                await _channel.Writer.WriteAsync(job);

                _logger.LogInformation(
                    "Vote queued — Voter {VoterId}, Position {PositionId}, Candidate {CandidateId}",
                    voterId, positionId, candidateId);

                return new ServiceResponse<string>
                {
                    Success = true,
                    Data = "Vote queued for processing.",
                    Message = "Your vote has been queued and will be recorded momentarily."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ConfirmAndCastVoteAsync for Voter {VoterId}", voterId);
                return new ServiceResponse<string>
                {
                    Success = false,
                    Message = "An error occurred while processing your vote. Please try again."
                };
            }
        }
        #endregion

        #region GetElectionsTakenByVoterAsync
        public async Task<ServiceResponse<List<ElectionEvent>>> GetElectionsTakenByVoterAsync(string voterId)
        {
            try
            {
                var elections = await _context.Votes
                    .Where(v => v.VoterId == voterId && v.HasVoted && !v.IsPenalized && v.Election != null)
                    .Select(v => v.Election!)
                    .Distinct()
                    .ToListAsync();

                return new ServiceResponse<List<ElectionEvent>>
                {
                    Success = true,
                    Data = elections,
                    Message = "Elections retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving taken elections for voter {VoterId}", voterId);
                return new ServiceResponse<List<ElectionEvent>> { Success = false, Message = "Failed to retrieve elections history." };
            }
        }
        #endregion

        #region GetVoterBallotHistoryAsync
        public async Task<ServiceResponse<List<Vote>>> GetVoterBallotHistoryAsync(string voterId, Guid electionId)
        {
            try
            {
                var history = await _context.Votes
                    .Where(v => v.VoterId == voterId && v.ElectionId == electionId)
                    .ToListAsync();

                return new ServiceResponse<List<Vote>>
                {
                    Success = true,
                    Data = history,
                    Message = "Ballot history retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ballot history for voter {VoterId}", voterId);
                return new ServiceResponse<List<Vote>> { Success = false, Message = "Failed to retrieve ballot history." };
            }
        }
        #endregion

        #region PenalizeVoterAsync
        public async Task<ServiceResponse<string>> PenalizeVoterAsync(string voterId, Guid electionId, string reason, string adminId)
        {
            try
            {
                var voteRecord = await _context.Votes
                    .FirstOrDefaultAsync(v => v.VoterId == voterId && v.ElectionId == electionId);

                if (voteRecord == null)
                {
                    voteRecord = new Vote
                    {
                        Id = Guid.NewGuid(),
                        VoterId = voterId,
                        ElectionId = electionId,
                        ConfirmationCode = "PENALIZED",
                        IsConfirmed = false,
                        HasVoted = false,
                        IsPenalized = true
                    };
                    _context.Votes.Add(voteRecord);
                }
                else
                {
                    voteRecord.ConfirmationCode = "PENALIZED";
                    voteRecord.IsConfirmed = false;
                    voteRecord.HasVoted = false;
                    voteRecord.IsPenalized = true;
                }

                await _context.SaveChangesAsync();
                _logger.LogWarning("Admin {AdminId} penalized voter {VoterId} for election {ElectionId}. Reason: {Reason}", adminId, voterId, electionId, reason);

                return new ServiceResponse<string>
                {
                    Success = true,
                    Data = "Penalized",
                    Message = "Voter successfully penalized."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error penalizing voter {VoterId}", voterId);
                return new ServiceResponse<string> { Success = false, Message = "Failed to penalize voter." };
            }
        }
        #endregion

        #region GetAllVotersAsync
        public async Task<ServiceResponse<PaginatedListViewModel<VoterDto>>> GetAllVotersAsync(string? searchTerm = null, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                string cacheKey = $"voters_p{pageNumber}_sz{pageSize}_term_{searchTerm?.Trim()?.ToLower() ?? "all"}";

                var cachedData = await _cache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    var cachedResult = JsonSerializer.Deserialize<PaginatedListViewModel<VoterDto>>(cachedData);
                    if (cachedResult != null)
                    {
                        return new ServiceResponse<PaginatedListViewModel<VoterDto>> { Success = true, Data = cachedResult };
                    }
                }

                var query = _context.Users.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    string term = searchTerm.Trim().ToLower();
                    query = query.Where(u => (u.Email != null && u.Email.ToLower().Contains(term)) ||
                                             (u.UserName != null && u.UserName.ToLower().Contains(term)));
                }

                int totalCount = await query.CountAsync();

                var users = await query
                    .OrderBy(u => u.Email)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var voterDtos = users.Select(u => new VoterDto
                {
                    Id = u.Id,
                    Email = u.Email ?? string.Empty,
                    FullName = u.UserName ?? string.Empty
                }).ToList();

                var result = new PaginatedListViewModel<VoterDto>
                {
                    Items = voterDtos,
                    TotalItems = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
                };
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), cacheOptions);

                return new ServiceResponse<PaginatedListViewModel<VoterDto>>
                {
                    Success = true,
                    Data = result,
                    Message = "Voters retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving paginated voters list.");
                return new ServiceResponse<PaginatedListViewModel<VoterDto>> { Success = false, Message = "Failed to retrieve voters." };
            }
        }
        #endregion

        #region GetPenalizedVotersAsync
        public async Task<ServiceResponse<PaginatedListViewModel<PenalizedVoterDto>>> GetPenalizedVotersAsync(int pageNumber, int pageSize)
        {
            try
            {
                var query = _context.Votes
                    .AsNoTracking()
                    .Where(v => v.IsPenalized)
                    .Join(_context.Users, v => v.VoterId, u => u.Id, (v, u) => new PenalizedVoterDto
                    {
                        Id = u.Id,
                        VoterEmail = u.Email ?? string.Empty,
                        VoterName = u.UserName ?? string.Empty,
                        ElectionId = v.ElectionId
                    });

                int totalCount = await query.CountAsync();

                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var result = new PaginatedListViewModel<PenalizedVoterDto>
                {
                    Items = items,
                    TotalItems = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                return new ServiceResponse<PaginatedListViewModel<PenalizedVoterDto>>
                {
                    Success = true,
                    Data = result,
                    Message = "Penalized voters retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving penalized voters list.");
                return new ServiceResponse<PaginatedListViewModel<PenalizedVoterDto>> { Success = false, Message = "Failed to retrieve penalized voters." };
            }
        }
        #endregion

        #region GetVotersWithPenalizationStatusAsync
        public async Task<ServiceResponse<PaginatedListViewModel<VoterPenalizationStatusDto>>> GetVotersWithPenalizationStatusAsync(Guid electionId, string? searchTerm = null, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var query = from u in _context.Users.AsNoTracking()
                            join v in _context.Votes.Where(vote => vote.ElectionId == electionId)
                            on u.Id equals v.VoterId into voterVotes
                            from vote in voterVotes.DefaultIfEmpty()
                            select new VoterPenalizationStatusDto
                            {
                                Id = u.Id,
                                VoterEmail = u.Email ?? string.Empty,
                                VoterName = u.UserName ?? string.Empty,
                                ElectionId = electionId,
                                IsPenalized = vote != null && vote.IsPenalized
                            };

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    string term = searchTerm.Trim().ToLower();
                    query = query.Where(x =>
                        (x.VoterEmail != null && x.VoterEmail.ToLower().Contains(term)) ||
                        (x.VoterName != null && x.VoterName.ToLower().Contains(term))
                    );
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(x => x.VoterEmail)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var result = new PaginatedListViewModel<VoterPenalizationStatusDto>
                {
                    Items = items,
                    TotalItems = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                return new ServiceResponse<PaginatedListViewModel<VoterPenalizationStatusDto>>
                {
                    Success = true,
                    Data = result,
                    Message = "Voters and their penalization status retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving voters with penalization status for election {ElectionId}", electionId);
                return new ServiceResponse<PaginatedListViewModel<VoterPenalizationStatusDto>> { Success = false, Message = "Failed to retrieve voter statuses." };
            }
        }
        #endregion

        #region BulkPenalizeVotersAsync
        public async Task<ServiceResponse<string>> BulkPenalizeVotersAsync(List<string> voterIds, Guid electionId, string reason, string adminId)
        {
            if (voterIds == null || !voterIds.Any())
            {
                return new ServiceResponse<string> { Success = false, Message = "No voters specified for bulk penalization." };
            }

            var safeVoterIds = new HashSet<string>(voterIds.Where(id => id != null)!);
            if (safeVoterIds.Count == 0)
            {
                return new ServiceResponse<string> { Success = false, Message = "No valid voters specified." };
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var existingVotes = await _context.Votes
                    .Where(v => v.ElectionId == electionId && v.VoterId != null && safeVoterIds.Contains(v.VoterId!))
                    .ToListAsync();

                var existingVoterIdsSet = existingVotes.Select(v => v.VoterId!).ToHashSet();

                foreach (var vote in existingVotes)
                {
                    vote.ConfirmationCode = "PENALIZED";
                    vote.IsConfirmed = false;
                    vote.HasVoted = false;
                    vote.IsPenalized = true;
                }

                var newVoterIdsToAdd = safeVoterIds.Where(id => !existingVoterIdsSet.Contains(id)).ToList();
                foreach (var voterId in newVoterIdsToAdd)
                {
                    _context.Votes.Add(new Vote
                    {
                        Id = Guid.NewGuid(),
                        VoterId = voterId,
                        ElectionId = electionId,
                        ConfirmationCode = "PENALIZED",
                        IsConfirmed = false,
                        HasVoted = false,
                        IsPenalized = true
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogWarning("⚠️ Admin {AdminId} executed bulk penalty for {Count} voters in election {ElectionId}. Reason: {Reason}",
                    adminId, safeVoterIds.Count, electionId, reason);

                return new ServiceResponse<string>
                {
                    Success = true,
                    Data = $"{safeVoterIds.Count} voters successfully penalized.",
                    Message = "Bulk penalization completed successfully."
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed during bulk penalization execution for election {ElectionId}", electionId);
                return new ServiceResponse<string> { Success = false, Message = "An error occurred during bulk penalization processing." };
            }
        }
        #endregion

        #region GenerateSecureConfirmationCode
        private string GenerateSecureConfirmationCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            char[] code = new char[6];

            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[6];
                rng.GetBytes(data);

                for (int i = 0; i < 6; i++)
                {
                    code[i] = chars[data[i] % chars.Length];
                }
            }

            return new string(code);
        }
        #endregion

        #region GetElectionResultsAsync
        public async Task<ServiceResponse<List<CandidateVoteDto>>> GetElectionResultsAsync(Guid electionEventId)
        {
            try
            {
                var results = await _context.Candidate
                    .Where(c => c.ElectionEventId == electionEventId)
                    .Select(c => new CandidateVoteDto
                    {
                        CandidateName = c.Name ?? "",
                        PositionName = c.Position != null ? c.Position.Name : "N/A",
                        VoteCount = _context.Votes.Count(v => v.CandidateId == c.Id && v.ElectionId == electionEventId && v.IsConfirmed && !v.IsPenalized)
                    })
                    .AsNoTracking()
                    .ToListAsync();

                return new ServiceResponse<List<CandidateVoteDto>>
                {
                    Success = true,
                    Data = results,
                    Message = "Results retrieved successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving election results for {ElectionEventId}", electionEventId);
                return new ServiceResponse<List<CandidateVoteDto>> { Success = false, Message = "Failed to retrieve results." };
            }
            #endregion
        }
    }
}