using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Repository.Services
{
    public class ElectionService : IElectionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ElectionService> _logger;
        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _accessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly iFileService _FileService;
        private readonly ITenantProvider _tenantProvider;
        private readonly IWebHostEnvironment _env;

        public ElectionService(
            AppDbContext context,
            ILogger<ElectionService> logger,
            IMemoryCache cache,
            ITenantProvider tenantProvider,
            IHttpContextAccessor accessor,
            UserManager<ApplicationUser> userManager, iFileService FileService, IWebHostEnvironment env)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
            _FileService= FileService?? throw new ArgumentNullException(nameof(FileService));
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

  

        public async Task<ServiceResponse<ElectionDto>> CreateElectionAsync(ElectionDto model, string userId)
        {
            var response = new ServiceResponse<ElectionDto>();

            if (model == null)
            {
                response.Success = false;
                response.Message = "Invalid election data package provided.";
                return response;
            }

            var activeTenantId = _tenantProvider.GetCurrentTenantId();

            var currentTenant = await _context.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == activeTenantId);

            if (currentTenant == null)
            {
                response.Success = false;
                response.Message = "Active workspace authorization context could not be verified.";
                return response;
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User session profile authentication failed.";
                return response;
            }

            bool isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool isTenantAdmin = await _userManager.IsInRoleAsync(user, "Official");

            if (!isSuperAdmin && !isTenantAdmin)
            {
                response.Success = false;
                response.Message = "Access Denied: Your profile permissions restrict election creation capabilities.";
                return response;
            }

            string TargetUrl = "/Election_Image/Profiles";
            string TargetFolder = "Election_Image";
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (model.UrlImage != null && model.UrlImage.Length > 0)
                {
                    string AllocatedName = await _FileService.RegisterAndQueueUploadAsync(model.UrlImage, FileType.Image, TargetFolder, CancellationToken.None);
                    TargetUrl = $"/{TargetFolder}/{AllocatedName}";
                }

                var election = new ElectionEvent
                {
                    Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
                    Title = model.Title,
                    Description = model.Description,
                    ElectionYear = DateTime.UtcNow.Year,
                    StartDate = model.StartDate,
                    EndDate = model.EndDate,
                    IsActive = false,
                    TenantId = currentTenant.Id,
                    Category = currentTenant.TenantCategory,
                    ImageUrl = TargetUrl
                };

                await _context.ElectionEvents.AddAsync(election);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // ── GENERATE THE COPYABLE REGISTRATION LINK ──
                var request = _accessor.HttpContext?.Request;
                if (request != null)
                {
                    var baseUrl = $"{request.Scheme}://{request.Host}";
                    // Points directly to the Candidate controller registration endpoint carrying the event ID
                    model.RegistrationLink = $"{baseUrl}/Candidate/CreateCandidate?electionEventId={election.Id}";
                }

                model.Id = election.Id;
                model.PhotoImage = TargetUrl;
                response.Data = model;
                response.Success = true;
                response.Message = "Election workspace successfully generated.";
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Fatal error inside CreateElectionAsync for User {UserId}", userId);

                response.Success = false;
                response.Message = "An unexpected error occurred while writing election properties.";
                response.Errors = new List<string> { ex.Message };
                return response;
            }
        }
        // Helper Method: Get Active Election by ID or Category (Cross-Tenant Aware)
        public async Task<ElectionEvent?> GetElectionByIdOrCategoryAsync(Guid electionId, TenantCategory? category = null)
        {
            // 1. Primary Lookup by explicit ID (ignoring tenant filters)
            if (electionId != Guid.Empty)
            {
                var election = await _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == electionId);

                if (election != null) return election;
            }

            // 2. Fallback Lookup by Category
            if (category.HasValue)
            {
                var categoryElection = await _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Category == category.Value && e.IsActive);

                if (categoryElection != null) return categoryElection;
            }

            // 3. System Fallback to Root Seeded Election
            return await _context.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Title == "General Presidential Election 2026");
        }

        public async Task<ServiceResponse<bool>> StartElectionAsync(Guid electionId, string userId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    response.Success = false;
                    response.Message = "User verification routine failed.";
                    return response;
                }

                bool isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
                bool isTenantAdmin = await _userManager.IsInRoleAsync(user, "Official");

                if (!isSuperAdmin && !isTenantAdmin)
                {
                    response.Success = false;
                    response.Message = "Access Denied: Unauthorized boundary access request rejected.";
                    return response;
                }

                // Added .IgnoreQueryFilters() to locate election cross-tenant
                var election = await _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == electionId);

                if (election == null)
                {
                    response.Success = false;
                    response.Message = "Target election record context could not be located.";
                    return response;
                }

                if (election.IsActive)
                {
                    response.Success = false;
                    response.Message = "Operation Aborted: Live poll operations are already running.";
                    return response;
                }

                election.IsActive = true;
                election.ElectionYear = DateTime.UtcNow.Year;
                election.StartDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                response.Success = true;
                response.Data = true;
                response.Message = "Polling operations successfully set to live status.";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal system error in StartElectionAsync for Election {ElectionId}", electionId);
                response.Success = false;
                response.Message = "Internal transaction failure disrupted poll activation.";
                response.Errors = new List<string> { ex.Message };
                return response;
            }
        }

        public async Task<ServiceResponse<bool>> EndElectionAsync(Guid electionId, string userId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    response.Success = false;
                    response.Message = "User verification routine failed.";
                    return response;
                }

                bool isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
                bool isTenantAdmin = await _userManager.IsInRoleAsync(user, "Official");

                if (!isSuperAdmin && !isTenantAdmin)
                {
                    response.Success = false;
                    response.Message = "Access Denied: Unauthorized boundary access request rejected.";
                    return response;
                }

                // Added .IgnoreQueryFilters() to locate election cross-tenant
                var election = await _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == electionId);

                if (election == null)
                {
                    response.Success = false;
                    response.Message = "Target election record context could not be located.";
                    return response;
                }

                if (!election.IsActive)
                {
                    response.Success = false;
                    response.Message = "Operation Aborted: This election is currently suspended or concluded.";
                    return response;
                }

                election.IsActive = false;
                election.EndDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                response.Success = true;
                response.Data = true;
                response.Message = "Live polling operations concluded successfully.";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal system error in EndElectionAsync for Election {ElectionId}", electionId);
                response.Success = false;
                response.Message = "Internal transaction failure disrupted poll termination.";
                response.Errors = new List<string> { ex.Message };
                return response;
            }
        }
        public async Task<ServiceResponse<List<ElectionEvent>>> GetPastElectionsAsync(Guid? tenantId = null)
        {
            var response = new ServiceResponse<List<ElectionEvent>>();
            try
            {
                var targetTenantId = tenantId ?? _tenantProvider.GetCurrentTenantId();

                var pastElections = await _context.ElectionEvents
                  .IgnoreQueryFilters()
              .Where(e => e.TenantId == targetTenantId &&
                   e.EndDate >= e.StartDate &&
                   e.EndDate <= DateTime.UtcNow &&
                   !e.IsActive)
                .OrderByDescending(e => e.EndDate)
                .Take(5)
                .AsNoTracking()
                 .ToListAsync();
                response.Success = true;
                response.Data = pastElections;
                response.Message = "Past elections retrieved successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving past elections for Tenant {TenantId}", tenantId);
                response.Success = false;
                response.Message = "An error occurred while fetching past elections.";
                response.Errors = new List<string> { ex.Message };
            }
            return response;
        }
        public async Task<ServiceResponse<PaginatedListViewModel<ElectionEvent>>> GetPagedElectionsAsync(int pageNumber, int pageSize, Guid? tenantId = null)
        {
            var response = new ServiceResponse<PaginatedListViewModel<ElectionEvent>>();
            try
            {
                pageNumber = pageNumber < 1 ? 1 : pageNumber;
                 pageSize = pageSize < 1 ? 10 : pageSize;

                var targetTenantId = tenantId ?? _tenantProvider.GetCurrentTenantId();

                var query = _context.ElectionEvents
                    .IgnoreQueryFilters()
                    .Where(e => e.TenantId == targetTenantId);

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(e => e.StartDate)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .AsNoTracking()
                    .ToListAsync();

                var pagedResult = new PaginatedListViewModel<ElectionEvent>
                {
                    Items = items,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalCount
                };

                response.Success = true;
                response.Data = pagedResult;
                response.Message = "Paginated elections retrieved successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving paginated elections for Tenant {TenantId}", tenantId);
                response.Success = false;
                response.Message = "An error occurred while fetching paginated elections.";
                response.Errors = new List<string> { ex.Message };
            }
            return response;

        }
        public async Task<ServiceResponse<List<ElectionEvent>>> GetElectionsTakenByVoterAsync(string voterId)
        {
            try
            {
                var electionIds = await _context.Votes
                    .Where(v => v.VoterId == voterId && v.HasVoted)
                    .Select(v => v.ElectionId)
                    .Distinct()
                    .ToListAsync();

                var elections = await _context.ElectionEvents
                    .Where(e => electionIds.Contains(e.Id))
                    .AsNoTracking()
                    .ToListAsync();

                return new ServiceResponse<List<ElectionEvent>>
                {
                    Success = true,
                    Data = elections,
                    Message = "Elections history fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching elections taken by voter {VoterId}", voterId);
                return new ServiceResponse<List<ElectionEvent>> { Success = false, Message = "Failed to retrieve voting history." };
            }
        }

        public async Task<ServiceResponse<List<Vote>>> GetVoterBallotHistoryAsync(string voterId, Guid electionId)
        {
            try
            {
                var votes = await _context.Votes
                    .Include(v => v.Candidate)
                    .Include(v => v.Positions)
                    .Where(v => v.VoterId == voterId && v.ElectionId == electionId && v.HasVoted)
                    .AsNoTracking()
                    .ToListAsync();

                return new ServiceResponse<List<Vote>>
                {
                    Success = true,
                    Data = votes,
                    Message = "Voter ballot breakdown fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ballot history for Voter {VoterId} in Election {ElectionId}", voterId, electionId);
                return new ServiceResponse<List<Vote>> { Success = false, Message = "Failed to fetch ballot breakdown." };
            }
        }

        public async Task<ServiceResponse<string>> PenalizeVoterAsync(string voterId, Guid electionId, string reason, string adminId)
        {
            try
            {
                var voterRecord = await _context.Votes
                    .FirstOrDefaultAsync(v => v.VoterId == voterId && v.ElectionId == electionId);

                if (voterRecord == null)
                {
                    // If they haven't interacted yet, create a penalty record tracker row
                    voterRecord = new Vote
                    {
                        Id = Guid.NewGuid(),
                        VoterId = voterId,
                        ElectionId = electionId,
                        HasVoted = false,
                        IsConfirmed = false
                    };
                    _context.Votes.Add(voterRecord);
                }

                // If you have a penalty property on your model, mark them. 
                // Alternatively, you can invalidate their active codes and flag them.
                voterRecord.ConfirmationCode = "PENALIZED";
                voterRecord.IsConfirmed = false;
                voterRecord.HasVoted = false; // Block voting rights

                // Optional: Log penalization audit event
                _logger.LogWarning("⚠️ Admin {AdminId} penalized voter {VoterId} for election {ElectionId}. Reason: {Reason}",
                    adminId, voterId, electionId, reason);

                await _context.SaveChangesAsync();

                return new ServiceResponse<string>
                {
                    Success = true,
                    Data = "Voter penalized successfully.",
                    Message = "The user has been barred from participating further in this election."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to penalize voter {VoterId}", voterId);
                return new ServiceResponse<string> { Success = false, Message = "An error occurred while trying to penalize the user." };
            }
        }
    }
}
