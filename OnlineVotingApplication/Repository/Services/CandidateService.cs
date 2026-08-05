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
using System;

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

            // 1. Guard Clause
            if (model == null)
            {
                response.Success = false;
                response.Message = "Invalid candidate data provided.";
                return response;
            }

            // 2. Identity & Access Validation
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist.";
                return response;
            }

            bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _UserManager.IsInRoleAsync(user, "Official");

            if (!isAdmin && !isOfficial)
            {
                response.Success = false;
                response.Message = "Only authorized users have access to this feature.";
                return response;
            }

            // 3. Database Constraints Validation (Name uniqueness)
            var candidateExists = await _appDbContext.Candidate
                .AnyAsync(x => x.Name == model.Name);

            if (candidateExists)
            {
                response.Success = false;
                response.Message = "Candidate already exists.";
                return response;
            }

            // ---MOVE FILE HANDLING OUTSIDE AND BEFORE THE TRANSACTION BLOCK ---
            string targetDatabasePathUrl = "/images/default-candidate.png"; // Set a clean web-ready fallback path
            string folderPathSegment = "Candidate_Profiles";

            try
            {
                if (model.CandidateImageUrl != null && model.CandidateImageUrl.Length > 0)
                {
                    // Streams file to disk, logs to PendingFiles, and wakes up background thread safely
                    string allocatedFileName = await _fileService.RegisterAndQueueUploadAsync(
                        file: model.CandidateImageUrl,
                        fileType: Enums.FileType.Image,
                        uploadFolder: folderPathSegment,
                        cancellationToken: CancellationToken.None
                    );

                   // Store the full relative web root folder path string for your HTML img tags
                    targetDatabasePathUrl = $"/{folderPathSegment}/{allocatedFileName}";
                }
            }
            catch (Exception fileEx)
            {
                response.Success = false;
                response.Message = $"File upload preprocessing engine failed: {fileEx.Message}";
                return response;
            }

            // 4. Execution Transaction (Handles candidate entity generation only)
            await using var transaction = await _appDbContext.Database.BeginTransactionAsync();

            try
            {
                var newCandidate = new Candidate
                {
                    Name = model.Name ?? "",
                    Manifesto = model.Manifesto ?? "",
                    CandidateImg = targetDatabasePathUrl, // Now cleanly saves the complete path string format
                    PartyId = model.PartyId,
                    PositionId = model.PositionId,
                    StateId = model.StateId,
                    LgaId = model.LgaId,
                    CreatedAt = DateTime.UtcNow,
                    isApproved = false,
                    CandidateID= $"CAN-{Guid.NewGuid().ToString().Substring(0, 6).ToUpperInvariant()}"
                };

                await _appDbContext.Candidate.AddAsync(newCandidate);
                await _appDbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                response.Success = true;
                response.Message = "Candidate created successfully.";
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // If the database insert completely fails, delete the stray file off the disk to avoid storage leaks
                if (targetDatabasePathUrl != "/images/default-candidate.png")
                {
                    string physicalFileCleanupPath = Path.Combine(_env.WebRootPath, folderPathSegment, Path.GetFileName(targetDatabasePathUrl));
                    _fileService.DeleteFile(physicalFileCleanupPath);
                }


                // THIS IS THE CRUCIAL CHANGE: Expose everything to see what is failing
                response.Success = false;
                response.Message = $"CRITICAL ERROR: {ex.Message} -> INNER: {ex.InnerException?.Message} -> STACK TRACE: {ex.StackTrace}";
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

            // Dynamic cache key uniquely identifies the requested page footprint
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
        public async Task<PaginatedListViewModel<PartyViewModel>> GetAllCandidateViaParty(Guid PartyId, int PageNumber = 1, int PageSize = 10)
        {
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);

            int skip = (PageNumber - 1) * PageSize;

            var Query = _appDbContext.Party.Where(m => m.Id == PartyId);
            var dbCount = Query.Count();

            string CacheKey = $"ref_All_Party_{PartyId}_{PageNumber}_{PageSize}";


            if (!_cache.TryGetValue(CacheKey, out List<PartyViewModel>? Party))
            {
                Party = await Query.OrderBy(m => m.Name).Select(m => new PartyViewModel
                {
                    Name = m.Name,
                    Description = m.Description,
                    LogoUrl = m.LogoUrl

                }).ToListAsync();
                _cache.Set(CacheKey, Party, TimeSpan.FromMinutes(5));

            }
            return new PaginatedListViewModel<PartyViewModel>
            {
                TotalItems = dbCount,
                Items = Party ?? Enumerable.Empty<PartyViewModel>(),
                PageNumber = PageNumber,
                PageSize = PageSize

            };

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
                var baseQuery = _appDbContext.Candidate.Where(m => !m.isDeleted && m.PositionId == positionId);
                response.TotalCount = await baseQuery.CountAsync();
                if (!_cache.TryGetValue(cacheKey, out List<CandidateViewModel>? cachedList))
            {
                cachedList = await baseQuery
                    .OrderBy(m => m.Name)
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
                var baseQuery = _appDbContext.Candidate.Where(m => !m.isDeleted && m.StateId == stateId);
                response.TotalCount = await baseQuery.CountAsync();
                if (!_cache.TryGetValue(cacheKey, out List<CandidateViewModel>? cachedList))
                {
                    
                    cachedList = await baseQuery
                        .OrderBy(m=>m.Name)
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
                // 1. Let the service handle uploading the NEW image and return the actual file name
                string allocatedFileName = await _fileService.RegisterAndQueueUploadAsync(
                    file: model.CandidateImageUrl,
                    fileType: Enums.FileType.Image,
                    uploadFolder: "Candidate_Profiles", // Matches your creation folder exactly
                    cancellationToken: CancellationToken.None
                );

                // 2. Temporarily hold onto the OLD path string before overwriting it
                string oldPathFromDb = existingCandidate.CandidateImg??"";

                // 3. Update the database property with the clean web URL matching the creation pattern
                existingCandidate.CandidateImg = $"/Candidate_Profiles/{allocatedFileName}";

                // 4. NOW safely clean up the old physical file from disk
                if (!string.IsNullOrEmpty(oldPathFromDb)
                    && !oldPathFromDb.Contains("default-candidate.png")
                    && !oldPathFromDb.Contains("default.png"))
                {
                    // Trim leading slash to safely combine paths on any operating system
                    string relativePath = oldPathFromDb.TrimStart('/');

                    // Reconstruct the exact physical file path on the server disk
                    string oldFilePath = Path.Combine(_env.WebRootPath, relativePath);

                    // Delete the old file from storage
                    _fileService.DeleteFile(oldFilePath);
                }         
            

       
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
