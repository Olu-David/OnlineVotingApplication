using Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace OnlineVotingApplication.Repository.Services
{
    public class VoteService : IVoteService
    {
        private readonly AppDbContext _context;
        private readonly IDistributedCache _cache;
        private readonly ILogger<VoteService> _logger;

        #region VoteService
        public VoteService(AppDbContext context, IDistributedCache cache, ILogger<VoteService> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
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
        public async Task<ServiceResponse<string>> GenerateAndQueueConfirmationCodeAsync(string voterId, Guid electionId)
        {
            try
            {
                string code = GenerateSecureConfirmationCode();

                var voteRecord = await _context.Votes
                    .FirstOrDefaultAsync(v => v.VoterId == voterId && v.ElectionId == electionId);

                if (voteRecord == null)
                {
                    voteRecord = new Vote
                    {
                        Id = Guid.NewGuid(),
                        VoterId = voterId,
                        ElectionId = electionId,
                        ConfirmationCode = code,
                        IsConfirmed = false,
                        HasVoted = false,
                        IsPenalized = false
                    };
                    _context.Votes.Add(voteRecord);
                }
                else
                {
                    voteRecord.ConfirmationCode = code;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Confirmation code {Code} generated for voter {VoterId}", code, voterId);

                return new ServiceResponse<string>
                {
                    Success = true,
                    Data = code,
                    Message = "Confirmation code generated and queued successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate confirmation code for voter {VoterId}", voterId);
                return new ServiceResponse<string> { Success = false, Message = "Failed to generate confirmation code." };
            }
        }
        #endregion

        #region ConfirmAndCastVoteAsync
        public async Task<ServiceResponse<string>> ConfirmAndCastVoteAsync(string voterId, Guid electionId, string enteredCode, Guid candidateId, Guid positionId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var voteRecord = await _context.Votes
                    .FirstOrDefaultAsync(v => v.VoterId == voterId && v.ElectionId == electionId);

                if (voteRecord == null || voteRecord.ConfirmationCode != enteredCode || voteRecord.IsPenalized)
                {
                    return new ServiceResponse<string> { Success = false, Message = "Invalid confirmation code, vote session, or voter is penalized." };
                }

                if (voteRecord.HasVoted)
                {
                    return new ServiceResponse<string> { Success = false, Message = "Voter has already cast their ballot." };
                }

                voteRecord.CandidateId = candidateId;
                voteRecord.PositionId = positionId;
                voteRecord.IsConfirmed = true;
                voteRecord.HasVoted = true;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ServiceResponse<string>
                {
                    Success = true,
                    Data = "Vote cast successfully.",
                    Message = "Your vote has been recorded securely."
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error confirming and casting vote for voter {VoterId}", voterId);
                return new ServiceResponse<string> { Success = false, Message = "An error occurred while casting your vote." };
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