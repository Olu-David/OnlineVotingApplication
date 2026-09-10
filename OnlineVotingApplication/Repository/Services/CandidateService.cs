using Azure;
using Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.CodeAnalysis.Elfie.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Mono.TextTemplating;
using OnlineVotingApplication.Areas;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Services;
using OnlineVotingApplication.SupaBase;
using Slugify;
using System;
using System.Text.Json;

namespace OnlineVotingApplication.Repository.Services
{

    public class CandidateService : iCandidateService
    {
        private readonly AppDbContext _appDbContext;
        // private readonly iFileService _fileService;
        private readonly ISupaBaseFileService _supabaseService;
        private readonly IDistributedCache _Cache;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHttpContextAccessor _httpContext;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CandidateService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly ITenantProvider _tenantProvider;
        private readonly IEmailService _emailService;

        public CandidateService(
            AppDbContext appDbContext,
            // iFileService fileService, 
            ISupaBaseFileService supabaseService,
            IDistributedCache Cache,
            UserManager<ApplicationUser> userManager,
            IHttpContextAccessor httpContext,
            IMemoryCache cache,
            ILogger<CandidateService> logger,
            IWebHostEnvironment env,
            ITenantProvider tenantProvider,
            IEmailService emailService)
        {
            _appDbContext = appDbContext;
            // _fileService = fileService;
            _supabaseService = supabaseService;
            _cache = cache;
            _userManager = userManager;
            _httpContext = httpContext;
            _Cache = Cache;
            _logger = logger;
            _env = env;
            _tenantProvider = tenantProvider;
            _emailService = emailService;
        }
        #region GetPaginatedPendingApplicationsAsync
        public async Task<PaginatedListViewModel<PendingApplicationViewModel>> GetPaginatedPendingApplicationsAsync(int pageNumber = 1, int pageSize = 10)
        {
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            string cacheKeyItems = $"ref_T{tenantId}_Pending_Apps_P{pageNumber}_S{pageSize}";
            string cacheKeyCount = $"ref_T{tenantId}_Pending_Apps_Count";

            bool isAdmin = _httpContext.HttpContext?.User.IsInRole("SuperAdmin") ?? false;

            // 1. Build the base query correctly based on roles (Declared outside the if-statement so it has proper scope)
            var baseQuery = _appDbContext.candidateInvitations
                .AsNoTracking()
                .Where(a => !a.IsUsed);

            if (!isAdmin)
            {
                if (tenantId == Guid.Empty)
                {
                    // Handle edge case where non-admin has no active tenant context
                    return new PaginatedListViewModel<PendingApplicationViewModel>
                    {
                        Items = new List<PendingApplicationViewModel>(),
                        TotalItems = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                // Restrict regular users to their specific tenant
                baseQuery = baseQuery.Where(a => a.TenantId == tenantId);
            }
            // Note: If isAdmin is true, it skips the filter and queries ALL tenants automatically!

            // 2. Fetch or Cache Total Count
            int totalCount;
            string? cachedCountStr = await _Cache.GetStringAsync(cacheKeyCount);

            if (cachedCountStr == null)
            {
                totalCount = await baseQuery.CountAsync();
                var countOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _Cache.SetStringAsync(cacheKeyCount, totalCount.ToString(), countOptions);
            }
            else
            {
                totalCount = int.Parse(cachedCountStr);
            }

            // 3. Fetch or Cache Paginated List
            List<PendingApplicationViewModel>? appList = null;
            string? cachedListJson = await _Cache.GetStringAsync(cacheKeyItems);

            if (cachedListJson != null)
            {
                appList = JsonSerializer.Deserialize<List<PendingApplicationViewModel>>(cachedListJson);
            }
            else
            {
                var rawApplications = await baseQuery
                    .Include(a => a.ElectionEvent)
                    .Include(a => a.Position)
                    .OrderByDescending(a => a.CreatedAt)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                // Map safely in-memory to your view model
                appList = rawApplications.Select(a => new PendingApplicationViewModel
                {
                    ApplicationId = a.Id,

                    ElectionEventId = a.ElectionEventId,
                    PositionId = a.PositionId,
                    CandidateEmail = a.CandidateEmail,
                    CandidateName = a.CandidateName,
                    ElectionTitle = a.ElectionEvent != null ? a.ElectionEvent.Title : "N/A",
                    PositionName = a.Position != null ? a.Position.Name : "General Position",
                    CreatedAt = a.CreatedAt
                }).ToList();

                string jsonToCache = JsonSerializer.Serialize(appList);
                var itemsOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _Cache.SetStringAsync(cacheKeyItems, jsonToCache, itemsOptions);
            }

            // 4. Return your custom PaginatedListViewModel
            return new PaginatedListViewModel<PendingApplicationViewModel>
            {
                Items = appList ?? new List<PendingApplicationViewModel>(),
                TotalItems = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
        #endregion

        #region SendCandidateInviteAsync
        public async Task<ServiceResponse<string>> SendCandidateInviteAsync(SendCandidateInvitation model)
        {
            var response = new ServiceResponse<string>();
            var tenantId = _tenantProvider.GetCurrentTenantId();

            bool isAdmin = _httpContext.HttpContext?.User.IsInRole("SuperAdmin") ?? false;
            bool isPlatformAdmin = _httpContext.HttpContext?.User.IsInRole("PlatformAdmin") ?? false;

            if (!isAdmin && isPlatformAdmin && tenantId == Guid.Empty)
            {
                response.Success = false;
                response.Message = "Only Authorized User can invite Candidate";
                return response;
            }

            var inviteItem = await _appDbContext.candidateInvitations
                .FirstOrDefaultAsync(m => m.CandidateEmail == model.CandidateEmail && !m.IsUsed);

            if (inviteItem == null)
            {
                response.Success = false;
                response.Message = "User has not applied yet";
                return response;
            }

            // Notice how it uses model.SecureLink right here!
            string subject = "Your Secure Candidate Registration Invitation";
            string message = $"Hello {inviteItem.CandidateName}, You have been invited to register as a candidate. Click the secure link below to complete your registration form:\n\n{model.SecureLink}\n\nNote: This link is unique to your email address ({inviteItem.CandidateEmail}) and can only be used once.";

            await _emailService.EmailSendAsync(inviteItem.CandidateEmail, subject, message);

            response.Data = inviteItem.Token;
            response.Message = "Invitation Link Has been Sent to Candidate";
            response.Success = true;
            return response;
        }
        #endregion




        #region CreateCandidateAsync
        public async Task<ServiceResponse<string>> CreateCandidateAsync(CandidateViewModel model, string userId, string token)
        {
            var response = new ServiceResponse<string>();

            if (model == null || model.ElectionEventId == Guid.Empty)
            {
                response.Success = false;
                response.Message = "Invalid candidate or election data provided.";
                return response;
            }

            var targetElection = await _appDbContext.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == model.ElectionEventId && !e.IsDeleted);

            if (targetElection == null)
            {
                response.Success = false;
                response.Message = "The targeted election event could not be found or has been disabled.";
                return response;
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User authentication verification failed.";
                return response;
            }

            CandidateInvitation? invitation = null;
            if (!string.IsNullOrEmpty(token))
            {
                invitation = await _appDbContext.candidateInvitations
                    .FirstOrDefaultAsync(i => i.Token == token && !i.IsUsed);

                if (invitation == null)
                {
                    response.Success = false;
                    response.Message = "This registration link is invalid, expired, or has already been used.";
                    return response;
                }

                if (invitation.ElectionEventId != targetElection.Id)
                {
                    response.Success = false;
                    response.Message = "This registration link does not belong to this election event.";
                    return response;
                }

                if (!string.Equals(user.Email, invitation.CandidateEmail, StringComparison.OrdinalIgnoreCase))
                {
                    response.Success = false;
                    response.Message = $"Access Denied: This secure link was issued exclusively to {invitation.CandidateEmail}. You are logged in with a different profile.";
                    return response;
                }
            }
            else
            {
                response.Success = false;
                response.Message = "A secure invitation token is required to register as a candidate.";
                return response;
            }

            Guid? effectiveTenantId = targetElection.TenantId;

            var candidateExists = await _appDbContext.Candidate
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Name == model.Name
                            && x.ElectionEventId == model.ElectionEventId
                            && x.TenantId == effectiveTenantId);

            if (candidateExists)
            {
                response.Success = false;
                response.Message = "A candidate with this name already exists in this contest configuration.";
                return response;
            }

            string targetDatabasePathUrl = "/images/default-candidate.png";
            string folderPathSegment = "Candidate_Profiles";
            string? uploadedFileUrlPath = null;
            bool roleAdded = false;

            // Upload file outside the transaction boundary to avoid holding locks during network I/O
            try
            {
                if (model.CandidateImageUrl != null && model.CandidateImageUrl.Length > 0)
                {
                    using var stream = model.CandidateImageUrl.OpenReadStream();
                    targetDatabasePathUrl = await _supabaseService.UploadFileAsync(
                        folderPathSegment,
                        model.CandidateImageUrl.FileName,
                        stream,
                        model.CandidateImageUrl.ContentType
                    );
                    uploadedFileUrlPath = targetDatabasePathUrl;
                }
            }
            catch (Exception fileEx)
            {
                response.Success = false;
                response.Message = $"Supabase file upload failed: {fileEx.Message}";
                return response;
            }

            var strategy = _appDbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _appDbContext.Database.BeginTransactionAsync();
                try
                {
                    if (await _userManager.IsInRoleAsync(user, "Voter"))
                    {
                        var addRoleResult = await _userManager.AddToRoleAsync(user, "Candidate");
                        if (!addRoleResult.Succeeded)
                        {
                            throw new Exception("Failed to upgrade user profile permissions to candidate.");
                        }
                        roleAdded = true;
                    }

                    var slugHelper = new SlugHelper();
                    var newCandidate = new Candidate
                    {
                        Id = Guid.NewGuid(),
                        TenantId = effectiveTenantId,
                        ElectionEventId = targetElection.Id,
                        Name = model.Name,
                        Manifesto = model.Manifesto,
                        CandidateImg = targetDatabasePathUrl,
                        Slug = "candidate-" + slugHelper.GenerateSlug(model.Name ?? ""),
                        PartyId = model.PartyId != Guid.Empty ? model.PartyId : null,
                        PositionId = model.PositionId != Guid.Empty ? model.PositionId : null,
                        StateId = model.StateId != Guid.Empty ? model.StateId : null,
                        LgaId = model.LgaId != Guid.Empty ? model.LgaId : null,
                        UserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        isApproved = true,
                        CandidateID = $"CAN-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}"
                    };

                    await _appDbContext.Candidate.AddAsync(newCandidate);

                    if (invitation != null)
                    {
                        invitation.IsUsed = true;
                        _appDbContext.candidateInvitations.Update(invitation);
                    }

                    await _appDbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    response.Data = newCandidate.CandidateID;
                    response.Success = true;
                    response.Message = "Nomination profile saved successfully and account role upgraded to candidate.";
                    return response;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    // Cleanup uploaded file if DB saving fails
                    if (!string.IsNullOrEmpty(uploadedFileUrlPath))
                    {
                        try
                        {
                            await _supabaseService.DeleteFileAsync(uploadedFileUrlPath, folderPathSegment);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogError(cleanupEx, "Failed to cleanup orphaned candidate image after DB error: {Path}", uploadedFileUrlPath);
                        }
                    }

                    // Rollback Identity role assignment if it was added during this failed attempt
                    if (roleAdded && await _userManager.IsInRoleAsync(user, "Candidate"))
                    {
                        try
                        {
                            await _userManager.RemoveFromRoleAsync(user, "Candidate");
                        }
                        catch (Exception roleEx)
                        {
                            _logger.LogError(roleEx, "Failed to rollback candidate role assignment for user: {UserId}", userId);
                        }
                    }

                    response.Success = false;
                    response.Message = $"Save Error: {ex.Message}";
                    return response;
                }
            });
        }
        #endregion


        #region CreateCandidateByOfficialAsync
        public async Task<ServiceResponse<string>> CreateCandidateByOfficialAsync(ManualCandidateCreationViewModel model, Guid currentTenantId, string officialUserId)
        {
            var response = new ServiceResponse<string>();

            if (model == null || model.ElectionEventId == Guid.Empty)
            {
                response.Success = false;
                response.Message = "Invalid candidate or election data provided.";
                return response;
            }

            var targetElection = await _appDbContext.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == model.ElectionEventId
                                        && e.TenantId == currentTenantId
                                        && !e.IsDeleted);

            if (targetElection == null)
            {
                response.Success = false;
                response.Message = "The selected election does not exist or does not belong to your organization.";
                return response;
            }

            var cleanEmail = model.CandidateEmail!.Trim().ToLower();
            var targetUser = await _userManager.FindByEmailAsync(cleanEmail);

            if (targetUser == null)
            {
                response.Success = false;
                response.Message = $"No registered account found matching email '{cleanEmail}'.";
                return response;
            }

            bool isPolitical = targetElection.Category == TenantCategory.Political;

            var candidateExists = await _appDbContext.Candidate
                .IgnoreQueryFilters()
                .AnyAsync(x => x.UserId == targetUser.Id && x.ElectionEventId == targetElection.Id);

            if (candidateExists)
            {
                response.Success = false;
                response.Message = "This user is already registered as a candidate for this election event.";
                return response;
            }

            string targetDatabasePathUrl = "/images/default-candidate.png";
            string folderPathSegment = "Candidate_Profiles";
            string? uploadedFileUrlPath = null;
            bool roleAdded = false;

            // Upload file outside the transaction boundary to avoid holding database locks during network I/O
            try
            {
                if (model.CandidateImage != null && model.CandidateImage.Length > 0)
                {
                    using var stream = model.CandidateImage.OpenReadStream();
                    targetDatabasePathUrl = await _supabaseService.UploadFileAsync(
                        folderPathSegment,
                        model.CandidateImage.FileName,
                        stream,
                        model.CandidateImage.ContentType
                    );
                    uploadedFileUrlPath = targetDatabasePathUrl;
                }
            }
            catch (Exception fileEx)
            {
                response.Success = false;
                response.Message = $"Supabase file upload failed: {fileEx.Message}";
                return response;
            }

            var strategy = _appDbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _appDbContext.Database.BeginTransactionAsync();
                try
                {
                    if (!await _userManager.IsInRoleAsync(targetUser, "Candidate"))
                    {
                        var addRoleResult = await _userManager.AddToRoleAsync(targetUser, "Candidate");
                        if (!addRoleResult.Succeeded)
                        {
                            throw new Exception("Failed to upgrade target user permissions to Candidate role.");
                        }
                        roleAdded = true;
                    }

                    var fullName = $"{targetUser.FullName}".Trim();
                    var candidateName = string.IsNullOrWhiteSpace(fullName) ? targetUser.UserName! : fullName;

                    var slugHelper = new SlugHelper();
                    var newCandidate = new Candidate
                    {
                        Id = Guid.NewGuid(),
                        TenantId = currentTenantId,
                        ElectionEventId = targetElection.Id,
                        UserId = targetUser.Id,
                        Name = candidateName,
                        Manifesto = model.Manifesto,
                        CandidateImg = targetDatabasePathUrl,
                        Slug = "candidate-" + slugHelper.GenerateSlug(candidateName),
                        PositionId = model.PositionId != Guid.Empty ? model.PositionId : null,
                        PartyId = isPolitical && model.PartyId.HasValue && model.PartyId != Guid.Empty ? model.PartyId : null,
                        StateId = isPolitical && model.StateId.HasValue && model.StateId != Guid.Empty ? model.StateId : null,
                        LgaId = isPolitical && model.LgaId.HasValue && model.LgaId != Guid.Empty ? model.LgaId : null,
                        CreatedAt = DateTime.UtcNow,
                        isApproved = true,
                        CandidateID = $"CAN-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}"
                    };

                    await _appDbContext.Candidate.AddAsync(newCandidate);
                    await _appDbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    response.Data = newCandidate.CandidateID;
                    response.Success = true;
                    response.Message = $"Candidate '{candidateName}' successfully created and assigned to {targetElection.Title}!";
                    return response;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    if (!string.IsNullOrEmpty(uploadedFileUrlPath))
                    {
                        try
                        {
                            await _supabaseService.DeleteFileAsync(uploadedFileUrlPath, folderPathSegment);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogError(cleanupEx, "Failed to cleanup orphaned candidate image after DB error: {Path}", uploadedFileUrlPath);
                        }
                    }

                    if (roleAdded && await _userManager.IsInRoleAsync(targetUser, "Candidate"))
                    {
                        try
                        {
                            await _userManager.RemoveFromRoleAsync(targetUser, "Candidate");
                        }
                        catch (Exception roleEx)
                        {
                            _logger.LogError(roleEx, "Failed to rollback candidate role assignment for user: {UserId}", targetUser.Id);
                        }
                    }

                    response.Success = false;
                    response.Message = $"Save Error: {ex.Message}";
                    return response;
                }
            });
        }
        #endregion

        #region CreateCandidateBySuperAdminAsync
        public async Task<ServiceResponse<string>> CreateCandidateBySuperAdminAsync(SuperAdminCandidateCreationViewModel model)
        {
            var response = new ServiceResponse<string>();

            if (model == null || model.TenantId == Guid.Empty || model.ElectionEventId == Guid.Empty)
            {
                response.Success = false;
                response.Message = "Invalid tenant, candidate, or election data provided.";
                return response;
            }

            var targetElection = await _appDbContext.ElectionEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == model.ElectionEventId
                                         && e.TenantId == model.TenantId
                                         && !e.IsDeleted);

            if (targetElection == null)
            {
                response.Success = false;
                response.Message = "The selected election does not exist or does not belong to the selected tenant.";
                return response;
            }

            var cleanEmail = model.CandidateEmail.Trim().ToLower();
            var targetUser = await _userManager.FindByEmailAsync(cleanEmail);

            if (targetUser == null)
            {
                response.Success = false;
                response.Message = $"No registered account found matching email '{cleanEmail}'.";
                return response;
            }

            bool isPolitical = targetElection.Category == TenantCategory.Political;

            var candidateExists = await _appDbContext.Candidate
                .IgnoreQueryFilters()
                .AnyAsync(x => x.UserId == targetUser.Id && x.ElectionEventId == targetElection.Id);

            if (candidateExists)
            {
                response.Success = false;
                response.Message = "This user is already registered as a candidate for this election event.";
                return response;
            }

            string targetDatabasePathUrl = "/images/default-candidate.png";
            string folderPathSegment = "Candidate_Profiles";
            string? uploadedFileUrlPath = null;
            bool roleAdded = false;

            // Perform external file storage upload outside the database transaction boundary
            try
            {
                if (model.CandidateImage != null && model.CandidateImage.Length > 0)
                {
                    using var stream = model.CandidateImage.OpenReadStream();
                    targetDatabasePathUrl = await _supabaseService.UploadFileAsync(
                        folderPathSegment,
                        model.CandidateImage.FileName,
                        stream,
                        model.CandidateImage.ContentType
                    );
                    uploadedFileUrlPath = targetDatabasePathUrl;
                }
            }
            catch (Exception fileEx)
            {
                response.Success = false;
                response.Message = $"Supabase file upload failed: {fileEx.Message}";
                return response;
            }

            var strategy = _appDbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _appDbContext.Database.BeginTransactionAsync();
                try
                {
                    if (!await _userManager.IsInRoleAsync(targetUser, "Candidate"))
                    {
                        var addRoleResult = await _userManager.AddToRoleAsync(targetUser, "Candidate");
                        if (!addRoleResult.Succeeded)
                        {
                            throw new Exception("Failed to upgrade target user permissions to Candidate role.");
                        }
                        roleAdded = true;
                    }

                    var fullName = $"{targetUser.FullName}".Trim();
                    var candidateName = string.IsNullOrWhiteSpace(fullName) ? targetUser.UserName! : fullName;

                    var slugHelper = new SlugHelper();
                    var newCandidate = new Candidate
                    {
                        Id = Guid.NewGuid(),
                        TenantId = model.TenantId,
                        ElectionEventId = targetElection.Id,
                        UserId = targetUser.Id,
                        Name = candidateName,
                        Manifesto = model.Manifesto,
                        CandidateImg = targetDatabasePathUrl,
                        Slug = "candidate-" + slugHelper.GenerateSlug(candidateName),
                        PositionId = model.PositionId != Guid.Empty ? model.PositionId : null,
                        PartyId = isPolitical && model.PartyId.HasValue && model.PartyId != Guid.Empty ? model.PartyId : null,
                        StateId = isPolitical && model.StateId.HasValue && model.StateId != Guid.Empty ? model.StateId : null,
                        LgaId = isPolitical && model.LgaId.HasValue && model.LgaId != Guid.Empty ? model.LgaId : null,
                        CreatedAt = DateTime.UtcNow,
                        isApproved = true,
                        CandidateID = $"CAN-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}"
                    };

                    await _appDbContext.Candidate.AddAsync(newCandidate);
                    await _appDbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    response.Data = newCandidate.CandidateID;
                    response.Success = true;
                    response.Message = $"Candidate '{candidateName}' successfully created and assigned to election '{targetElection.Title}'!";
                    return response;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    if (!string.IsNullOrEmpty(uploadedFileUrlPath))
                    {
                        try
                        {
                            await _supabaseService.DeleteFileAsync(uploadedFileUrlPath, folderPathSegment);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogError(cleanupEx, "Failed to cleanup orphaned candidate image after DB error: {Path}", uploadedFileUrlPath);
                        }
                    }

                    if (roleAdded && await _userManager.IsInRoleAsync(targetUser, "Candidate"))
                    {
                        try
                        {
                            await _userManager.RemoveFromRoleAsync(targetUser, "Candidate");
                        }
                        catch (Exception roleEx)
                        {
                            _logger.LogError(roleEx, "Failed to rollback candidate role assignment for user: {UserId}", targetUser.Id);
                        }
                    }

                    response.Success = false;
                    response.Message = $"Save Error: {ex.Message}";
                    return response;
                }
            });
        }
        #endregion

        #region ClearCandidateCache
        public void ClearCandidateCache(int pageNumber, int pageSize)
        {
            string cacheKey = $"ref_All_Candidates_P{pageNumber}_S{pageSize}";
            _cache.Remove(cacheKey);
        }
        #endregion


        #region GetAllCandidates
        public async Task<PaginatedListViewModel<CandidateViewModel>> GetAllCandidates(int PageNumber = 1, int PageSize = 10)
        {
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);
            int skip = (PageNumber - 1) * PageSize;

            string cacheKeyItems = $"ref_T{tenantId}_All_Candidate_P{PageNumber}_S{PageSize}";
            string cacheKeyCount = $"ref_T{tenantId}_All_Candidate_Count";

            var baseQuery = _appDbContext.Candidate
                .AsNoTracking()
                .Where(m => m.TenantId == tenantId && !m.isDeleted);

            // 1. Fetch or Cache Total Count
            int totalCount;
            string? cachedCountStr = await _Cache.GetStringAsync(cacheKeyCount);

            if (cachedCountStr == null)
            {
                totalCount = await baseQuery.CountAsync();
                var countOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _Cache.SetStringAsync(cacheKeyCount, totalCount.ToString(), countOptions);
            }
            else
            {
                totalCount = int.Parse(cachedCountStr);
            }

            // 2. Fetch or Cache Candidate List
            List<CandidateViewModel>? candidateList = null;
            string? cachedListJson = await _Cache.GetStringAsync(cacheKeyItems);

            if (cachedListJson != null)
            {
                candidateList = JsonSerializer.Deserialize<List<CandidateViewModel>>(cachedListJson);
            }
            else
            {
                // Fetch raw database entities with related fields included
                var rawCandidates = await baseQuery
                    .Include(m => m.Party)
                    .Include(m => m.Position)
                    .Include(m => m.State)
                    .Include(m => m.CustomValues)
                    .Include(m => m.GalleryPhotos)
                    .OrderBy(m => m.Name)
                    .Skip(skip)
                    .Take(PageSize)
                    .ToListAsync();

                // Map view models in-memory (where .ToDictionary and projections work safely)
                candidateList = rawCandidates.Select(m => new CandidateViewModel
                {
                    CandidateID = m.Id,
                    Name = m.Name,
                    Manifesto = m.Manifesto,
                    image = m.CandidateImg,
                    PartyName = m.Party != null ? m.Party.Name : "Unassigned",
                    Position = m.Position != null ? m.Position.Name : "Unassigned",
                    StateName = m.State != null ? m.State.Name : "National",

                    // Mapped dynamic answers key-value pairs safely in-memory
                    DynamicAnswers = m.CustomValues != null
            ? m.CustomValues.ToDictionary(cv => cv.FieldId, cv => cv.Value)
            : new Dictionary<Guid, string>(),

                    // Mapped gallery photos collection safely in-memory
                    GalleryPhotoss = m.GalleryPhotos != null
            ? m.GalleryPhotos.ToList()
            : new List<CandidateGallery>()
                }).ToList();
                string jsonToCache = JsonSerializer.Serialize(candidateList);
                var itemsOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _Cache.SetStringAsync(cacheKeyItems, jsonToCache, itemsOptions);
            }

            // 3. Return the fully populated paginated view model list
            return new PaginatedListViewModel<CandidateViewModel>
            {
                Items = candidateList ?? new List<CandidateViewModel>(),
                TotalItems = totalCount,
                PageNumber = PageNumber,
                PageSize = PageSize
            };
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
        #endregion



        #region GetAllCandidateViaParty
        public async Task<PaginatedListViewModel<CandidateViewModel>> GetAllCandidateViaParty(Guid partyId, int pageNumber = 1, int pageSize = 10)
        {
            // Ensure page numbers and sizes stay within positive boundaries
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);

            int skip = (pageNumber - 1) * pageSize;

            // Filter by the candidate's PartyId foreign key and exclude deleted entries
            var query = _appDbContext.Candidate.Where(m => m.PartyId == partyId && !m.isDeleted);

            // Create unique cache keys scoped specifically to this party and page configuration
            string cacheKey = $"ref_All_Party_{partyId}_{pageNumber}_{pageSize}";
            string cacheCountKey = $"Party_Candidate_Count_{partyId}";

            int totalCount;
            string? cacheCountString = await _Cache.GetStringAsync(cacheCountKey);

            if (string.IsNullOrEmpty(cacheCountString))
            {
                totalCount = await query.CountAsync();

                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _Cache.SetStringAsync(cacheCountKey, totalCount.ToString(), cacheOptions);
            }
            else
            {
                totalCount = int.Parse(cacheCountString);
            }

            List<CandidateViewModel>? candidateList = null;
            string? cachedJsonList = await _Cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedJsonList))
            {
                candidateList = JsonSerializer.Deserialize<List<CandidateViewModel>>(cachedJsonList);
            }
            else
            {
                // Query database with pagination and map directly to CandidateViewModel
                candidateList = await query
                    .OrderBy(m => m.Name)
                    .Skip(skip)
                    .Take(pageSize)
                    .Select(m => new CandidateViewModel
                    {
                        CandidateID = m.Id,
                        Name = m.Name,
                        Manifesto = m.Manifesto,
                        image = m.CandidateImg,

                        StateName = m.State != null ? m.State.Name : string.Empty,
                        LgaName = m.LGA != null ? m.LGA.Name : string.Empty
                    })
                    .ToListAsync();

                // Save the result to Redis cache if data was fetched from the database
                if (candidateList != null && candidateList.Count > 0)
                {
                    var cacheOptions = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    };
                    string serializedList = JsonSerializer.Serialize(candidateList);
                    await _Cache.SetStringAsync(cacheKey, serializedList, cacheOptions);
                }
            }

            // Return the paginated model package to the controller
            return new PaginatedListViewModel<CandidateViewModel>
            {
                TotalItems = totalCount,
                Items = candidateList ?? new List<CandidateViewModel>(),
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
        #endregion


        #region GetCandidateByPartyAsync

        public async Task<ServiceResponse<PaginatedListViewModel<CandidateViewModel>>> GetCandidateByPartyAsync(Guid partyId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<PaginatedListViewModel<CandidateViewModel>>();

            // 1. Sanitize bounds safely
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            // 2. Define Cache Keys
            string cacheKeyData = $"ref_Candidates_Party_{partyId}_P{pageNumber}_S{pageSize}";
            string cacheKeyCount = $"ref_Candidates_Party_{partyId}_Count";

            try
            {
                _logger.LogInformation("Fetching candidates for Party ID: {PartyId}", partyId);

                // Build the base query skeleton (deferred execution)
                var baseQuery = _appDbContext.Candidate
                    .AsNoTracking()
                    .Where(m => !m.isDeleted && m.Party!.Id == partyId);

                // 3. Get Total Records Count (Cached)
                if (!_cache.TryGetValue(cacheKeyCount, out int totalCount))
                {
                    totalCount = await baseQuery.CountAsync();
                    _cache.Set(cacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
                }

                // 4. Get Sliced Records List (Cached)
                if (!_cache.TryGetValue(cacheKeyData, out List<CandidateViewModel>? cachedList))
                {
                    cachedList = await baseQuery
                        .OrderBy(m => m.Name) // Always order before Skip/Take
                        .Skip(skip)
                        .Take(pageSize)
                        .Select(m => new CandidateViewModel
                        {
                            CandidateID = m.Id,
                            Name = m.Name,
                            Manifesto = m.Manifesto,
                            image = m.CandidateImg,
                            PartyName = m.Party != null ? (m.Party.Name ?? "Independent") : "Independent",
                            Position = m.Position != null ? (m.Position.Name ?? "Unassigned") : "Unassigned",
                            StateName = m.State != null ? (m.State.Name ?? "National") : "National"
                        })
                        .ToListAsync();

                    _cache.Set(cacheKeyData, cachedList, TimeSpan.FromMinutes(5));
                }

                // 5. Structure the wrapped data output
                var paginatedResult = new PaginatedListViewModel<CandidateViewModel>
                {
                    TotalItems = totalCount,
                    Items = cachedList ?? Enumerable.Empty<CandidateViewModel>(),
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                if (cachedList == null || cachedList.Count == 0)
                {
                    response.Data = paginatedResult;
                    response.Success = false;
                    response.Message = "No Candidate Found";
                    return response;
                }

                response.Data = paginatedResult;
                response.Success = true;
                response.Message = "Candidate detail found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Candidate Details not found or deleted for Party: {PartyId}", partyId);

                response.Success = false;
                response.Message = "System error";
                response.Data = new PaginatedListViewModel<CandidateViewModel>
                {
                    TotalItems = 0,
                    Items = Enumerable.Empty<CandidateViewModel>(),
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                return response;
            }
        }
        #endregion


        #region GetCandidateByPositionAsync
        public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByPositionAsync(Guid positionId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            // 1. Sanitize bounds
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            // 2. Uniform cache keys
            string cacheKeyCount = $"ref_Candidates_Position_T{tenantId}_I{positionId}_Count";
            string cacheKeyItems = $"ref_Candidates_Position_T{tenantId}_I{positionId}_P{pageNumber}_S{pageSize}";

            try
            {
                _logger.LogInformation("Fetching candidates for Position ID: {PositionId}, Tenant ID: {TenantId}", positionId, tenantId);

                // 3. Secure tenant isolation in the base query
                var baseQuery = _appDbContext.Candidate
                    .AsNoTracking()
                    .Where(m => !m.isDeleted && m.TenantId == tenantId && m.PositionId == positionId);

                // 4. Cache or fetch Total Count
                if (!_cache.TryGetValue(cacheKeyCount, out int totalCount))
                {
                    totalCount = await baseQuery.CountAsync();
                    _cache.Set(cacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
                }

                response.TotalCount = totalCount;

                // Early exit for zero results—treat as a valid state, not an error
                if (totalCount == 0)
                {
                    response.Data = Enumerable.Empty<CandidateViewModel>();
                    response.Success = true;
                    response.Message = "No candidates found for the selected position.";
                    return response;
                }

                // 5. Cache or fetch paginated items
                if (!_cache.TryGetValue(cacheKeyItems, out List<CandidateViewModel>? cachedList) || cachedList == null)
                {
                    cachedList = await baseQuery
                        .OrderBy(m => m.Name)
                        .Skip(skip)
                        .Take(pageSize)
                        .Select(m => new CandidateViewModel
                        {
                            CandidateID = m.Id,
                            Name = m.Name,
                            Manifesto = m.Manifesto,
                            image = m.CandidateImg,
                            PartyName = m.Party != null ? m.Party.Name : "Independent",
                            Position = m.Position != null ? m.Position.Name : "Unassigned",
                            StateName = m.State != null ? m.State.Name : "National"
                        })
                        .ToListAsync();

                    if (cachedList.Count > 0)
                    {
                        _cache.Set(cacheKeyItems, cachedList, TimeSpan.FromMinutes(5));
                    }
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
        #endregion







        #region GetCandidateByStateAsync
        public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByStateAsync(Guid? stateId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();

            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            // 1. Sanitize bounds
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            // 2. Safe string key for nullable Guid
            string safeStateId = stateId?.ToString() ?? "ALL";
            string cacheKeyItems = $"ref_Candidates_State_T{tenantId}_S{safeStateId}_P{pageNumber}_S{pageSize}";
            string cacheKeyCount = $"ref_Candidates_State_T{tenantId}_S{safeStateId}_Count";

            try
            {
                _logger.LogInformation("Fetching candidates for State ID: {StateId}, Tenant ID: {TenantId}", stateId, tenantId);

                // 3. Add AsNoTracking for read performance
                var baseQuery = _appDbContext.Candidate
                    .AsNoTracking()
                    .Where(m => !m.isDeleted && m.TenantId == tenantId && m.StateId == stateId);

                // 4. Cache total count properly
                if (!_cache.TryGetValue(cacheKeyCount, out int totalCount))
                {
                    totalCount = await baseQuery.CountAsync();
                    _cache.Set(cacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
                }

                response.TotalCount = totalCount;

                // Early exit if 0 items to avoid useless caching/queries
                if (totalCount == 0)
                {
                    response.Data = Enumerable.Empty<CandidateViewModel>();
                    response.Success = true;
                    response.Message = "No candidates found for this constituency.";
                    return response;
                }

                // 5. Fetch or Cache Paginated Items
                if (!_cache.TryGetValue(cacheKeyItems, out List<CandidateViewModel>? cachedList) || cachedList == null)
                {
                    cachedList = await baseQuery
                        .OrderBy(m => m.Name)
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

                    if (cachedList.Count > 0)
                    {
                        _cache.Set(cacheKeyItems, cachedList, TimeSpan.FromMinutes(5));
                    }
                }

                response.Data = cachedList;
                response.Success = true;
                response.Message = "Candidate details found successfully.";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve candidates for State ID: {StateId}", stateId);

                response.Success = false;
                response.Message = "A system error occurred while retrieving candidate data.";
                response.Data = Enumerable.Empty<CandidateViewModel>();
                return response;
            }
        }
        #endregion


        #region GetCandidateByLgaAsync
        public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetCandidateByLgaAsync(Guid? lgaId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            string safeLgaId = lgaId?.ToString() ?? "ALL";
            string cacheKeyCount = $"ref_Candidates_LGA_T{tenantId}_L{safeLgaId}_Count";
            string cacheKeyItems = $"ref_Candidates_LGA_T{tenantId}_L{safeLgaId}_P{pageNumber}_S{pageSize}";

            try
            {
                _logger.LogInformation("Fetching candidates for LGA ID: {LgaId}, Tenant ID: {TenantId}", lgaId, tenantId);

                var baseQuery = _appDbContext.Candidate
                    .AsNoTracking()
                    .Where(m => !m.isDeleted && m.TenantId == tenantId && m.LgaId == lgaId);

                if (!_cache.TryGetValue(cacheKeyCount, out int totalCount))
                {
                    totalCount = await baseQuery.CountAsync();
                    _cache.Set(cacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
                }

                response.TotalCount = totalCount;

                if (totalCount == 0)
                {
                    response.Data = Enumerable.Empty<CandidateViewModel>();
                    response.Success = true;
                    response.Message = "No candidates found for this constituency.";
                    return response;
                }

                if (!_cache.TryGetValue(cacheKeyItems, out List<CandidateViewModel>? cachedList) || cachedList == null)
                {
                    cachedList = await baseQuery
                        .OrderBy(m => m.Name)
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

                    if (cachedList.Count > 0)
                    {
                        _cache.Set(cacheKeyItems, cachedList, TimeSpan.FromMinutes(5));
                    }
                }

                response.Data = cachedList;
                response.Success = true;
                response.Message = "Candidate details found successfully.";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve candidates for LGA ID: {LgaId}", lgaId);
                response.Success = false;
                response.Message = "System error occurred while retrieving filtered data.";
                response.Data = Enumerable.Empty<CandidateViewModel>();
                return response;
            }
        }
        #endregion

        #region GetAllSoftDeletedCandidate
        public async Task<ServiceResponse<IEnumerable<CandidateViewModel>>> GetAllSoftDeletedCandidate(string userId, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<IEnumerable<CandidateViewModel>>();
            Guid tenantId = _tenantProvider.GetCurrentTenantId();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User context validation failed.";
                return response;
            }

            bool isAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            if (!isAdmin)
            {
                response.Success = false;
                response.Message = "User does not have authorization to access this feature.";
                return response;
            }

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            var retentionThreshold = DateTime.UtcNow.AddDays(-30);
            string cacheKeyCount = $"ref_SoftDeleted_Count_T{tenantId}";
            string cacheKeyItems = $"ref_SoftDeleted_T{tenantId}_P{pageNumber}_S{pageSize}";

            try
            {
                var baseQuery = _appDbContext.Candidate
                    .AsNoTracking()
                    .Where(m => m.isDeleted && m.TenantId == tenantId && m.DeletedAt >= retentionThreshold);

                if (!_cache.TryGetValue(cacheKeyCount, out int totalCount))
                {
                    totalCount = await baseQuery.CountAsync();
                    _cache.Set(cacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
                }

                response.TotalCount = totalCount;

                if (totalCount == 0)
                {
                    response.Data = Enumerable.Empty<CandidateViewModel>();
                    response.Success = true;
                    response.Message = "Trash bin registry is currently empty.";
                    return response;
                }

                if (!_cache.TryGetValue(cacheKeyItems, out List<CandidateViewModel>? cachedList) || cachedList == null)
                {
                    cachedList = await baseQuery
                        .OrderByDescending(m => m.DeletedAt) // Required for Skip/Take
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

                    if (cachedList.Count > 0)
                    {
                        _cache.Set(cacheKeyItems, cachedList, TimeSpan.FromMinutes(5));
                    }
                }

                response.Data = cachedList;
                response.Success = true;
                response.Message = "Soft-deleted entries retrieved successfully.";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve soft-deleted entries for Tenant ID: {TenantId}", tenantId);
                response.Success = false;
                response.Message = "System error occurred while retrieving filtered data.";
                response.Data = Enumerable.Empty<CandidateViewModel>();
                return response;
            }
        }


        #endregion


        #region RestoreCandidateDeleteAsync

        public async Task<ServiceResponse<bool>> RestoreCandidateDeleteAsync(Guid Id, string UserId)
        {
            var response = new ServiceResponse<bool>();
            var user = await _userManager.FindByIdAsync(UserId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                return response;
            }

            bool isAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _userManager.IsInRoleAsync(user, "Official");

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

        #endregion



        #region SoftDeleteCandidateAsync
        public async Task<ServiceResponse<bool>> SoftDeleteCandidateAsync(Guid candidateId, string userId, CancellationToken cancellationToken = default)
        {
            var response = new ServiceResponse<bool>();

            // 1. Fetch user and their roles in one single query, eliminating the separate DbContext lookup
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                response.Data = false;
                return response;
            }

            // 2. Validate roles efficiently
            bool isAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _userManager.IsInRoleAsync(user, "Official");

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
        #endregion


        #region UpdateCandidateAsync

        public async Task<ServiceResponse<string>> UpdateCandidateAsync(UpdateCandidateViewModel model, string Id, CancellationToken token)
        {
            var response = new ServiceResponse<string>();
            var user = await _userManager.FindByIdAsync(Id);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                return response;
            }

            bool isAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _userManager.IsInRoleAsync(user, "Official");

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

            string folderPathSegment = "Candidate_Profiles";

            if (model.CandidateImageUrl != null && model.CandidateImageUrl.Length > 0)
            {
                // 1. Temporarily hold onto the OLD image URL/path before overwriting it
                string oldPathFromDb = existingCandidate.CandidateImg ?? "";

                // 2. Upload the new image to Supabase Storage
                string newPublicUrl;
                using (var stream = model.CandidateImageUrl.OpenReadStream())
                {
                    newPublicUrl = await _supabaseService.UploadFileAsync(
                        folderPathSegment,
                        model.CandidateImageUrl.FileName,
                        stream,
                        model.CandidateImageUrl.ContentType
                    );
                }

                // 3. Update the database property with the new public URL returned by Supabase
                existingCandidate.CandidateImg = newPublicUrl;

                // 4. Clean up the old file from Supabase storage if it's not a default fallback image
                if (!string.IsNullOrEmpty(oldPathFromDb)
                    && !oldPathFromDb.Contains("default-candidate.png")
                    && !oldPathFromDb.Contains("default.png"))
                {
                    try
                    {
                        // Pass both the old file URL/path and the bucket name to your Supabase service
                        await _supabaseService.DeleteFileAsync(oldPathFromDb, folderPathSegment);
                    }
                    catch
                    {
                        // Suppress cleanup failures so they don't block the profile update
                    }
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
        #endregion
    }
}

    