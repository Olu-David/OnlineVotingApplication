using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Mono.TextTemplating;
using OnlineVotingApplication.Areas;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Repository.Services
{
    public class CandidateService : iCandidateService
    {
        private readonly AppDbContext _appDbContext;
        private readonly iFileService _fileService;
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly IHttpContextAccessor _httpContext;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CandidateService> _logger;
        private readonly IWebHostEnvironment _env;

        public CandidateService(AppDbContext appDbContext, iFileService fileService, UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContext, IMemoryCache cache, ILogger<CandidateService> logger, IWebHostEnvironment env)
        {
            _appDbContext = appDbContext;
            _fileService = fileService;
            _UserManager = userManager;
            _httpContext = httpContext;
            _cache = cache;
            _logger = logger;
            _env = env;
        }

        public async Task<ServiceResponse<string>> CreateCandidateAsync(CandidateViewModel model, string userId)
        {
            var response = new ServiceResponse<string>();

            var user = await _UserManager.FindByIdAsync(userId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                return response;
            }

            bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _UserManager.IsInRoleAsync(user, "Official");

            if (!isAdmin && !isOfficial)
            {
                response.Success = false;
                response.Message = "Only authorized users have access to this feature";
                return response;
            }

            var officialUser = await _appDbContext.Users
                .FirstOrDefaultAsync(x => x.OfficialStaffId == model.OfficialStaffId);

            if (officialUser == null)
            {
                response.Success = false;
                response.Message = "Access denied. Invalid official credentials.";
                return response;
            }

            var candidateExists = await _appDbContext.Candidate
                .AnyAsync(x => x.Name == model.Name);

            if (candidateExists)
            {
                response.Success = false;
                response.Message = "Candidate already exists.";
                return response;
            }

            await using var transaction = await _appDbContext.Database.BeginTransactionAsync();

            try
            {
                string fileName = "default-candidate.png";

                if (model.CandidateImageUrl != null)
                {
                    // 1. Generate the name layout you want
                    fileName = $"{Guid.NewGuid()}_{Path.GetFileName(model.CandidateImageUrl.FileName)}";

                    // 2. Pass it down to your service alongside CancellationToken.None to clear the syntax error
                    await _fileService.RegisterAndQueueUploadAsync(
                        model.CandidateImageUrl,
                        Enums.FileType.Image,
                        CancellationToken.None
                    );
                }

                var newCandidate = new Candidate
                {
                    Name = model.Name,
                    Manifesto = model.Manifesto,
                    CandidateImg = fileName, // This matches what the file service saved!
                    PartyId = model.PartyId,
                    PositionId = model.PositonId,
                    StateId = model.StateId,
                    LgaId = model.LgaId,
                    CreatedAt = DateTime.UtcNow,
                    isApproved = false
                };

                await _appDbContext.Candidate.AddAsync(newCandidate);
                await _appDbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                response.Success = true;
                response.Message = "Candidate created successfully.";
                return response;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();

                response.Success = false;
                response.Message = "An unexpected error occurred.";
                return response;
            }
        }
        public void ClearCandidateCache(int pageNumber, int pageSize)
        {
            string cacheKey = $"ref_All_Candidates_P{pageNumber}_S{pageSize}";
            _cache.Remove(cacheKey);
        }

        public async Task<List<CandidateViewModel>> GetAllCandidates(int PageNumber, int PageSize)
        {
            int skip = (PageNumber - 1) * PageSize;

            // Fix: Dynamic cache key uniquely identifies the requested page footprint
            string cachekey = $"ref_All_Candidate_P{PageNumber}_S{PageSize}";

            if (!_cache.TryGetValue(cachekey, out List<CandidateViewModel>? dto))
            {
                dto = await _appDbContext.Candidate
                    .Select(m => new CandidateViewModel
                    {
                        CandidateID = m.Id,
                        Name = m.Name,
                        Manifesto = m.Manifesto,
                        image = m.CandidateImg,
                        PartyName = m.Party != null ? m.Party.Name : "Unassigned",
                        Position = m.Position != null ? m.Position.Name : "null",
                        StateName = m.State != null ? m.State.Name : "Unassigned"
                    })
                    .Skip(skip)
                    .Take(PageSize)
                    .ToListAsync();

                _cache.Set(cachekey, dto, TimeSpan.FromMinutes(5));
            }

            return dto!;
        }


        public async Task<ServiceResponse<CandidateViewModel>> GetCandidateByIdAsync(Guid id)
        {
            var response = new ServiceResponse<CandidateViewModel>();
            var result = await _appDbContext.Candidate.Where(m => m.Id == id).Select(m => new CandidateViewModel
            {
                Name = m.Name,
                Manifesto = m.Manifesto,
                image = m.CandidateImg,
                PartyName = m.Party != null ? m.Party.Name : "Unassigned",
                Position = m.Position != null ? m.Position.Name : "null",
                StateName = m.State != null ? m.State.Name : "Unassigned"


            }).FirstOrDefaultAsync();
            if (result == null)
            {
                response.Data = result;
                response.Success = false;
                return response;
            }
            response.Data = result;
            response.Success = true;
            return response;

        }

public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByPartyAsync(Guid partyId, int pageNumber = 1, int pageSize = 10)
    {
        var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        int skip = (pageNumber - 1) * pageSize;

        string cacheKey = $"ref_Candidates_Party_{partyId}_P{pageNumber}_S{pageSize}";

        try
        {
            _logger.LogInformation("Fetching candidates for Party ID: {PartyId}", partyId);

            if (!_cache.TryGetValue(cacheKey, out List<CandidateViewModel>? cachedList))
            {
                cachedList = await _appDbContext.Candidate
                    .Where(m => !m.isDeleted && m.Party.Id == partyId)
                    .Select(m => new CandidateViewModel
                    {
                        CandidateID = m.Id,
                        Name = m.Name,
                        Manifesto = m.Manifesto,
                        image = m.CandidateImg,
                        PartyName = m.Party.Name ?? "Independent",
                        Position = m.Position.Name ?? "Unassigned",
                        StateName = m.State.Name ?? "National"
                    })
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                _cache.Set(cacheKey, cachedList, TimeSpan.FromMinutes(5));
            }

            if (cachedList == null || cachedList.Count == 0)
            {
                response.Data = cachedList ?? new List<CandidateViewModel>();
                response.Success = false;
                response.Message = "No Candidate Found";
                return response;
            }

            response.Data = cachedList;
            response.Success = true;
            response.Message = "Candidate detail found";
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Candidate Details not found or deleted for Party: {PartyId}", partyId);

            response.Success = false;
            response.Message = "System error";
            response.Data = Enumerable.Empty<CandidateViewModel>();
            return response;
        }
    }

    public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByPositionAsync(Guid positionId, int pageNumber = 1, int pageSize = 10)
    {
        var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();
            
           pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize; 

        string cacheKey = $"ref_Candidates_Position_{positionId}_P{pageNumber}_S{pageSize}";

        try
        {
            _logger.LogInformation("Fetching candidates for Position ID: {PositionId}", positionId);

            if (!_cache.TryGetValue(cacheKey, out List<CandidateViewModel>? cachedList))
            {
                cachedList = await _appDbContext.Candidate
                    .Where(m => !m.isDeleted && m.Position.Id == positionId)
                    .Select(m => new CandidateViewModel
                    {
                        CandidateID = m.Id,
                        Name = m.Name,
                        Manifesto = m.Manifesto,
                        image = m.CandidateImg,
                        PartyName = m.Party.Name ?? "Independent",
                        Position = m.Position.Name ?? "Unassi gned",
                        StateName = m.State.Name ?? "National"
                    })
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                _cache.Set(cacheKey, cachedList, TimeSpan.FromMinutes(5));
            }

            if (cachedList == null || cachedList.Count == 0)
            {
                response.Data = cachedList ?? new List<CandidateViewModel>();
                response.Success = false;
                response.Message = "No candidates found for the selected position.";
                return response;
            }

            response.Data = cachedList;
            response.Success = true;
            response.Message = $"Successfully retrieved {cachedList.Count} candidate(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while fetching candidates for Position: {PositionId}", positionId);

            response.Success = false;
            response.Message = "A system error occurred. Please try again later.";
            response.Data = Enumerable.Empty<CandidateViewModel>();
        }

        return response;
    }



    public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByStateAsync(Guid? stateId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize; 
            
            string cacheKey = $"ref_Candidates_State_{stateId}_P{pageNumber}_S{pageSize}";

            try
            {
                if (!_cache.TryGetValue(cacheKey, out List<CandidateViewModel>? cachedList))
                {
                    cachedList = await _appDbContext.Candidate
                        .Where(m => !m.isDeleted && m.StateId == stateId)
                        .Select(m => new CandidateViewModel
                        {
                            CandidateID = m.Id,
                            Name = m.Name,
                            Manifesto = m.Manifesto,
                            image = m.CandidateImg,
                            PartyName = m.Party != null ? m.Party.Name : "Unassigned",
                            Position = m.Position != null ? m.Position.Name : "Unassigned",
                            StateName = m.State != null ? m.State.Name : "Unassigned"
                        })
                        .Skip(skip)
                        .Take(pageSize)
                        .ToListAsync();

                    _cache.Set(cacheKey, cachedList, TimeSpan.FromMinutes(5));
                }

                if (cachedList == null || !cachedList.Any())
                {
                    response.Data = cachedList;
                    response.Success = false;
                    response.Message = "No Candidate Found for this constituency";
                    return response;
                }

                response.Data = cachedList;
                response.Success = true;
                response.Message = "Candidate details found successfully";
                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "System error occurred while retrieving filtered data";
                _logger.LogError(ex, "Failed to retrieve candidates for State ID: {StateId}", stateId);
                return response;
            }
        }
        public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByLgaAsync(Guid? LgaId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();

            // 1.  validation (Math.Max ensures 1 or higher)
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            // 2. Define base query to reuse for both Count and Data
            var baseQuery = _appDbContext.Candidate.Where(m => !m.isDeleted && m.LgaId == LgaId);

            // 3. Add this: Calculate count before Skip/Take
            response.TotalCount = await baseQuery.CountAsync();

            string CacheKey = $"ref_AllCandidateByLga_{LgaId}_{pageNumber}_{pageSize}";
            if (!_cache.TryGetValue(CacheKey, out List<CandidateViewModel>? cachedList))
            {
                cachedList = await baseQuery
                          .OrderBy(m => m.Name) // Required for Skip/Take
                          .Skip(skip)
                          .Take(pageSize)
                          .Select(m => new CandidateViewModel
                          {
                              CandidateID = m.Id,
                              Name = m.Name,
                              Manifesto = m.Manifesto,
                              image = m.CandidateImg,
                              PartyName = m.Party != null ? m.Party.Name : "Unassigned",
                              Position = m.Position != null ? m.Position.Name : "Unassigned",
                              StateName = m.State != null ? m.State.Name : "Unassigned"
                          })
                          .ToListAsync();

                _cache.Set(CacheKey, cachedList, TimeSpan.FromMinutes(5));
            }

            if (cachedList == null || !cachedList.Any())
            {
                response.Data = cachedList;
                response.Success = false;
                response.Message = "No Candidate Found for this constituency";
                return response;
            }

            response.Data = cachedList;
            response.Success = true;
            response.Message = "Candidate details found successfully";
            return response;
        }
        public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetAllSoftDeletedCandidate(string userId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();

            // 1. Authorisation Handrails
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User context validation failed.";
                return response;
            }

            bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
            if (!isAdmin)
            {
                response.Success = false;
                response.Message = "User does not have authorization to access this feature.";
                return response;
            }

            // 2.  Boundary Handrails 
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            int skip = (pageNumber - 1) * pageSize;

            // 30-day retention calendar calculation threshold
            var retentionThreshold = DateTime.UtcNow.AddDays(-30);
            string cacheKey = $"ref_All_SoftDeleted_Candidate_{pageNumber}_{pageSize}";

            try
            {
                if (!_cache.TryGetValue(cacheKey, out List<CandidateViewModel>? cachedList))
                {
                    // 3. FIXED: Changed .OrderBy to .Where to filter database rows correctly
                    cachedList = await _appDbContext.Candidate
                        .AsNoTracking()
                        .Where(m => m.isDeleted && m.DeletedAt >= retentionThreshold)
                        .Select(m => new CandidateViewModel
                        {
                            CandidateID = m.Id,
                            Name = m.Name,
                            Manifesto = m.Manifesto,
                            image = m.CandidateImg,
                            PartyName = m.Party != null ? m.Party.Name : "Unassigned",
                            Position = m.Position != null ? m.Position.Name : "Unassigned",
                            StateName = m.State != null ? m.State.Name : "Unassigned"
                        })
                        .Skip(skip)
                        .Take(pageSize)
                        .ToListAsync();

                    _cache.Set(cacheKey, cachedList, TimeSpan.FromMinutes(5));
                }

                if (cachedList == null || !cachedList.Any())
                {
                    response.Data = Enumerable.Empty<CandidateViewModel>();
                    response.Success = true; // Technically a successful lookup, just empty
                    response.Message = "Trash bin registry is currently empty.";
                    return response;
                }

                response.Data = cachedList;
                response.Success = true;
                response.Message = "Soft-deleted entries retrieved successfully.";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve soft-deleted entries.");
                response.Success = false;
                response.Message = "System error occurred while retrieving filtered data.";
                return response;
            }
        }



        public async Task<ServiceResponse<bool>> RestoreCandidateDeleteAsync(Guid Id, string UserId)
        {
            var response = new ServiceResponse<bool>();
            var user = await _UserManager.FindByIdAsync(UserId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                return response;
            }

            bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _UserManager.IsInRoleAsync(user, "Official");

            if (!isAdmin && !isOfficial)
            {
                response.Success = false;
                response.Message = "Only authorized users have access to this feature";
                return response;
            }

            var officialUser = await _appDbContext.Users.FirstOrDefaultAsync(x => x.OfficialStaffId == UserId);

            if (officialUser == null)
            {
                response.Success = false;
                response.Message = "Access denied. Invalid official credentials.";
                return response;
            }
            var existingCandidate = await _appDbContext.Candidate.Where(x => x.Id == Id && x.isDeleted).FirstOrDefaultAsync();
            if (existingCandidate == null)
            {
                response.Success = false;
                response.Message = "Candidate doesnt Exist here";
                return response;
            }
            var DeletedAt = existingCandidate.DeletedAt ?? DateTime.UtcNow;
            var daySinceDeleted = (DateTime.UtcNow - DeletedAt).TotalDays;
            if (!existingCandidate.DeletedAt.HasValue)
            {

                response.Success = false;
                response.Message = "Candidate wasnt never deleted";
                return response;
            }



            if (daySinceDeleted > 30)
            {
                response.Success = false;
                response.Message = "Limit Exceeded: Cannot restore after 30 days";
                return response;
            }

            // If we are here, it's within 30 days
            existingCandidate.isDeleted = false;
            existingCandidate.DeletedAt = null;

            await _appDbContext.SaveChangesAsync();
            response.Success = true;
            response.Message = "Product has been restored successfully";
            return response;

        }
        public async Task<ServiceResponse<bool>> SoftDeleteCandidateAsync(Guid candidateId, string userId, CancellationToken cancellationToken = default)
        {
            var response = new ServiceResponse<bool>();

            // 1. Fetch user and their roles in one single query, eliminating the separate DbContext lookup
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                response.Data = false;
                return response;
            }

            // 2. Validate roles efficiently
            bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _UserManager.IsInRoleAsync(user, "Official");

            if (!isAdmin && !isOfficial)
            {
                response.Success = false;
                response.Message = "Only authorized users have access to this feature";
                response.Data = false;
                return response;
            }

            // 3. Fetch the candidate using FindAsync or FirstOrDefaultAsync
            var candidate = await _appDbContext.Candidate
                .FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken);

            if (candidate == null)
            {
                response.Success = false;
                response.Message = "Candidate not found.";
                response.Data = false;
                return response;
            }

            // 4. Perform the manual soft delete mutation
            candidate.isDeleted = true;
            candidate.DeletedAt = DateTime.UtcNow;

            await _appDbContext.SaveChangesAsync(cancellationToken);

            response.Success = true;
            response.Message = "Candidate moved to trash. It will be permanently deleted automatically in 30 days.";
            response.Data = true;
            return response;
        }


        public async Task<ServiceResponse<string>> UpdateCandidateAsync(UpdateCandidateViewModel model, string Id, CancellationToken token)
        {
            var response = new ServiceResponse<string>();
            var user = await _UserManager.FindByIdAsync(Id);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                return response;
            }

            bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _UserManager.IsInRoleAsync(user, "Official");

            if (!isAdmin && !isOfficial)
            {
                response.Success = false;
                response.Message = "Only authorized users have access to this feature";
                return response;
            }

            var officialUser = await _appDbContext.Users
                .FirstOrDefaultAsync(x => x.OfficialStaffId == model.OfficialStaffId, token);

            if (officialUser == null)
            {
                response.Success = false;
                response.Message = "Access denied. Invalid official credentials.";
                return response;
            }

            // 1. GET EXISTING CANDIDATE
            var existingCandidate = await _appDbContext.Candidate
                .FirstOrDefaultAsync(x => x.Id == model.CandidateID, token);

            if (existingCandidate == null)
            {
                response.Success = false;
                response.Message = "Candidate does not exist";
                return response;
            }

            if (model.CandidateImageUrl != null && model.CandidateImageUrl.Length > 0)
            {
                // 1. Delete old image physically from disk if it's not the default
                if (!string.IsNullOrEmpty(existingCandidate.CandidateImg)
                    && existingCandidate.CandidateImg != "default-candidate.png"
                    && existingCandidate.CandidateImg != "default.png")
                {
                    // Reconstruct the full physical file path so your DeleteFile method can find it
                    string oldFilePath = Path.Combine(_env.WebRootPath, "Optimized_Images", existingCandidate.CandidateImg);
                    _fileService.DeleteFile(oldFilePath);
                }

                // 2. Generate the unique synchronized name
                string newFileName = $"{Guid.NewGuid()}_{Path.GetFileName(model.CandidateImageUrl.FileName)}";

                // 3. Upload new image using your custom optional override parameter
                await _fileService.RegisterAndQueueUploadAsync(
                    model.CandidateImageUrl,
                    Enums.FileType.Image,
                    token
                );

                // 4. Update the database property with the computed name
                existingCandidate.CandidateImg = newFileName;
            }

            // 3. UPDATE OTHER FIELDS
            existingCandidate.Name = model.Name;
            existingCandidate.Manifesto = model.Manifesto;
            existingCandidate.PartyId = model.PartyId;
            existingCandidate.PositionId = model.PositonId; // Matches your view model spelling typo
            existingCandidate.StateId = model.StateId;

            // 4. SAVE CHANGES
            await _appDbContext.SaveChangesAsync(token);

            response.Success = true;
            response.Message = "Candidate updated successfully";

            return response;
        }


        //    public async Task<bool> DeleteCandidatePermanentlyAsync(Guid candidateId, string userId, CancellationToken cancellationToken)
        //    {

        //        var user = await _UserManager.FindByIdAsync(userId);

        //        if (user == null)
        //        {

        //            return false;
        //        }

        //        bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
        //        bool isOfficial = await _UserManager.IsInRoleAsync(user, "Official");

        //        if (!isAdmin && !isOfficial)
        //        {

        //            return false;
        //        }

        //        var officialUser = await _appDbContext.Users
        //            .FirstOrDefaultAsync(x => x.OfficialStaffId == userId);

        //        if (officialUser == null)
        //        {

        //            return false;
        //        }

        //        var CutOffDate= DateTime.UtcNow.AddDays(-30);

        //        var CandidateToDelete = await _appDbContext.Candidate
        //         .FirstOrDefaultAsync(p => p.Id == candidateId &&
        //                                   p.DeletedAt != null &&
        //                                   p.DeletedAt < CutOffDate);

        //        // 3. VALIDATION & EXECUTION
        //        if (CandidateToDelete == null)
        //        {
        //            // Either product doesn't exist, or it hasn't stayed 30 days in trash yet
        //            return false;
        //        }

        //        _appDbContext.Candidate.Remove(CandidateToDelete);
        //        await _appDbContext.SaveChangesAsync();


        //        return true;
        //    }
        //}
    }

}
