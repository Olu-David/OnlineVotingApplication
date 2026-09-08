using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.SupaBase;

namespace OnlineVotingApplication.Repository.Services
{
    public class PartyService : IPartyService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PartyService> _logger;
        private readonly ISupaBaseFileService _supaBaseFileService;
        private readonly IMemoryCache _cache;

        #region PartyService
        public PartyService(
            UserManager<ApplicationUser> userManager,
            AppDbContext context,
            IWebHostEnvironment env,
            ILogger<PartyService> logger,
            ISupaBaseFileService supaBaseFileService,
            IMemoryCache cache)
        {
            _userManager = userManager;
            _context = context;
            _env = env;
            _logger = logger;
            _supaBaseFileService = supaBaseFileService;
            _cache = cache;
        }
        #endregion

        #region CreatePartyAsync
        public async Task<ServiceResponse<string>> CreatePartyAsync(PartyViewModel model, string Id)
        {
            var response = new ServiceResponse<string>();
            var user = await _userManager.FindByIdAsync(Id);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found";
                return response;
            }

            bool IsAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool IsOfficial = await _userManager.IsInRoleAsync(user, "Official");
            if (!IsAdmin && !IsOfficial)
            {
                response.Success = false;
                response.Message = "Unable to perform this function, Contact Admin or Official";
                return response;
            }

            var cleanedtrim = model.Name?.Trim();
            var CheckExistingParty = await _context.Party.AnyAsync(m => m.Name == cleanedtrim);
            if (CheckExistingParty)
            {
                response.Success = false;
                response.Message = "Party Exist Try Another Name";
                return response;
            }

            string PartyLogoPathUrl = "/logo/default-Party_Logo.png";
            string bucketName = "party-logos";

            try
            {
                if (model.PartyLogo != null && model.PartyLogo.Length > 0)
                {
                    string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(model.PartyLogo.FileName)}";

                    using var stream = model.PartyLogo.OpenReadStream();
                    PartyLogoPathUrl = await _supaBaseFileService.UploadFileAsync(
                        bucketName,
                        fileName,
                        stream,
                        model.PartyLogo.ContentType
                    );
                }
            }
            catch (Exception fileEx)
            {
                response.Success = false;
                response.Message = $"Supabase file upload failed: {fileEx.Message}";
                return response;
            }

            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var newParty = new Party
                    {
                        Name = cleanedtrim ?? string.Empty, // Used the cleaned version here too!
                        Description = model.Description,
                        LogoUrl = PartyLogoPathUrl
                    };

                    await _context.Party.AddAsync(newParty);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    response.Success = true;
                    response.Message = "Party created successfully.";
                    return response;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    if (!string.IsNullOrEmpty(PartyLogoPathUrl) && !PartyLogoPathUrl.Contains("default-Party_Logo.png"))
                    {
                        try
                        {
                            await _supaBaseFileService.DeleteFileAsync(PartyLogoPathUrl, bucketName);
                        }
                        catch (Exception cleanupEx)
                        {
                            // Log the failure to delete orphaned file, but don't mask the original error
                            _logger.LogError(cleanupEx, "Failed to cleanup orphaned logo file after database error: {Path}", PartyLogoPathUrl);
                        }
                    }

                    response.Success = false;
                    response.Message = $"CRITICAL ERROR: {ex.Message} -> INNER: {ex.InnerException?.Message}";
                    return response;
                }
            });
        }
        #endregion


        #region AllPartyAsync
        public async Task<PaginatedListViewModel<PartyViewModel>> AllPartyAsync(int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            string cacheKeyCount = "Allparty_party_count";
            string cacheKeyData = $"ref_All_Party_{pageNumber}_{pageSize}";

            if (!_cache.TryGetValue(cacheKeyCount, out int totalCount))
            {
                totalCount = await _context.Party.CountAsync();
                _cache.Set(cacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
            }

            if (!_cache.TryGetValue(cacheKeyData, out List<PartyViewModel>? parties))
            {
                parties = await _context.Party
                    .AsNoTracking()
                    .OrderBy(m => m.Name)
                    .Skip(skip)
                    .Take(pageSize)
                    .Select(m => new PartyViewModel
                    {
                        Name = m.Name,
                        Description = m.Description,
                        LogoUrl = m.LogoUrl
                    })
                    .ToListAsync();

                _cache.Set(cacheKeyData, parties, TimeSpan.FromMinutes(5));
            }

            return new PaginatedListViewModel<PartyViewModel>
            {
                TotalItems = totalCount,
                Items = parties ?? Enumerable.Empty<PartyViewModel>(),
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
        #endregion

        #region AllSoftDeleteAsync
        public async Task<PaginatedListViewModel<PartyViewModel>> AllSoftDeleteAsync(int PageNumber = 1, int PageSize = 10)
        {
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);
            int skip = (PageNumber - 1) * PageSize;

            string CacheKeyItems = $"deleted_party_{PageNumber}_{PageSize}";
            string CacheKeyCount = $"deleted_party_count";

            var queryDb = _context.Party.AsNoTracking().Where(m => m.IsDeleted);

            if (!_cache.TryGetValue(CacheKeyCount, out int totalCount))
            {
                totalCount = await queryDb.CountAsync();
                _cache.Set(CacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
            }

            if (!_cache.TryGetValue(CacheKeyItems, out List<PartyViewModel>? deletedParties))
            {
                deletedParties = await queryDb
                    .OrderBy(m => m.Name)
                    .Skip(skip)
                    .Take(PageSize)
                    .Select(m => new PartyViewModel
                    {
                        Name = m.Name,
                        Description = m.Description,
                        LogoUrl = m.LogoUrl
                    })
                    .ToListAsync();

                _cache.Set(CacheKeyItems, deletedParties, TimeSpan.FromMinutes(5));
            }

            return new PaginatedListViewModel<PartyViewModel>
            {
                TotalItems = totalCount,
                Items = deletedParties ?? new List<PartyViewModel>(),
                PageNumber = PageNumber,
                PageSize = PageSize
            };
        }
        #endregion

        #region EditPartyAsync
        public async Task<ServiceResponse<string>> EditPartyAsync(EditPartyViewModel model, string Id)
        {
            var response = new ServiceResponse<string>();
            var user = await _userManager.FindByIdAsync(Id);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found";
                return response;
            }

            bool IsAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool IsOfficial = await _userManager.IsInRoleAsync(user, "Official");
            if (!IsAdmin && !IsOfficial)
            {
                response.Success = false;
                response.Message = "Unable to perform this function, Contact Admin or Official";
                return response;
            }

            var CheckExistingParty = await _context.Party.FirstOrDefaultAsync(m => m.Id == model.Id);
            if (CheckExistingParty == null)
            {
                response.Success = false;
                response.Message = "Party not found";
                return response;
            }

            string bucketName = "party-logos";

            if (model.LogoFile != null && model.LogoFile.Length > 0)
            {
                string oldFilePath = CheckExistingParty.LogoUrl ?? "";
                string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(model.LogoFile.FileName)}";

                using var stream = model.LogoFile.OpenReadStream();
                string newLogoUrl = await _supaBaseFileService.UploadFileAsync(
                    bucketName,
                    fileName,
                    stream,
                    model.LogoFile.ContentType
                );

                CheckExistingParty.LogoUrl = newLogoUrl;

                if (!string.IsNullOrEmpty(oldFilePath) && !oldFilePath.Contains("default-Party_Logo.png"))
                {
                    await _supaBaseFileService.DeleteFileAsync(oldFilePath, bucketName);
                }
            }

            CheckExistingParty.Name = model.Name;
            CheckExistingParty.Description = model.Description;

            _context.Party.Update(CheckExistingParty);
            await _context.SaveChangesAsync();

            response.Success = true;
            response.Message = "Party Updated Successfully";
            return response;
        }
        #endregion

        #region SoftDeletePartyAsync
        public async Task<ServiceResponse<bool>> SoftDeletePartyAsync(string Id, Guid PartyID)
        {
            var response = new ServiceResponse<bool>();
            var user = await _userManager.FindByIdAsync(Id);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found";
                return response;
            }

            bool IsAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool IsOfficial = await _userManager.IsInRoleAsync(user, "Official");
            if (!IsAdmin && !IsOfficial)
            {
                response.Success = false;
                response.Message = "Unable to perform this function, Contact Admin or Official";
                return response;
            }

            var CheckExistingParty = await _context.Party.FirstOrDefaultAsync(m => m.Id == PartyID);
            if (CheckExistingParty == null)
            {
                response.Success = false;
                response.Message = "Party not found";
                return response;
            }

            CheckExistingParty.IsDeleted = true;
            CheckExistingParty.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            response.Success = true;
            return response;
        }
        #endregion

        #region RestoreDeletedParty
        public async Task<ServiceResponse<string>> RestoreDeletedParty(string Id, Guid PartyID)
        {
            var response = new ServiceResponse<string>();
            var user = await _userManager.FindByIdAsync(Id);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found";
                return response;
            }

            bool IsAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool IsOfficial = await _userManager.IsInRoleAsync(user, "Official");
            if (!IsAdmin && !IsOfficial)
            {
                response.Success = false;
                response.Message = "Unable to perform this function, Contact Admin or Official";
                return response;
            }

            var CheckExistingParty = await _context.Party.FirstOrDefaultAsync(m => m.Id == PartyID);
            if (CheckExistingParty == null)
            {
                response.Success = false;
                response.Message = "Party not found";
                return response;
            }

            if (!CheckExistingParty.DeletedAt.HasValue)
            {
                response.Success = false;
                response.Message = "Party was never deleted";
                return response;
            }

            var deletedAt = CheckExistingParty.DeletedAt.Value;
            var DaysSinceDeleted = (DateTime.UtcNow - deletedAt).TotalDays;

            if (DaysSinceDeleted > 30)
            {
                response.Success = false;
                response.Message = "Limit Exceeded: Cannot restore after 30 days";
                return response;
            }

            CheckExistingParty.IsDeleted = false;
            CheckExistingParty.DeletedAt = null;

            await _context.SaveChangesAsync();
            response.Success = true;
            response.Message = "Party has been restored successfully";
            return response;
        }
        #endregion
    }
}