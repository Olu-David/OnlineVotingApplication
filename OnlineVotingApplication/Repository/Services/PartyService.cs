
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Collections;

namespace OnlineVotingApplication.Repository.Services
{
    public class PartyService : IPartyService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PartyService> _logger;
        private readonly iFileService _fileService;
        private readonly IMemoryCache _cache;

        public PartyService(UserManager<ApplicationUser> userManager, AppDbContext context, IWebHostEnvironment env, ILogger<PartyService> logger, iFileService fileService, IMemoryCache cache)
        {
            _userManager = userManager;
            _context = context;
            _env = env;
            _logger = logger;
            _fileService = fileService;
            _cache = cache;
        }

        public async Task<ServiceResponse<string>> CreatePartyAsync(PartyViewModel model, string Id)
        {
            var response = new ServiceResponse<string>();
            //Create Authorize first cuz only SuperAdmin or Official can Create party
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
            //Checked if Paarty Exists;
            var cleanedtrim = model.Name?.Trim();
            var CheckExistingParty = await _context.Party.AnyAsync(m => m.Id == model.Id && m.Name == cleanedtrim);
            if (CheckExistingParty)
            {
                response.Success = false;
                response.Message = "Party Exist Try Another Name";
                return response;
            }

            string PartyLogoPathUrl = "/logo/default-Party_Logo.png";
            string PartyFolder = "Party_Logo";
            try
            {
              
                if (model.PartyLogo != null || model.PartyLogo?.Length > 0)
                {
                    string allocatedPath = await _fileService.RegisterAndQueueUploadAsync(file: model.PartyLogo, fileType: Enums.FileType.Image, PartyFolder, cancellationToken: CancellationToken.None);
                    PartyLogoPathUrl = $"/{PartyFolder}/{allocatedPath}";
                }
            }
            catch (Exception fileEx)
            {
                response.Success = false;
                response.Message = $"File upload preprocessing engine failed: {fileEx.Message}";
                return response;
            }
        
            var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var newParty = new Party
                {
                    Name = model.Name,
                    Description = model.Description,
                    LogoUrl = PartyLogoPathUrl
                    
                };
                await _context.Party.AddAsync(newParty);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                response.Success = true;
                response.Message = "Candidate created successfully.";
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // If the database insert completely fails, delete the stray file off the disk to avoid storage leaks
                if (PartyLogoPathUrl != "/logo/default/Party_Logo.png")
                {
                    string physicalFileCleanupPath = Path.Combine(_env.WebRootPath, PartyFolder, Path.GetFileName(PartyLogoPathUrl));
                    _fileService.DeleteFile(physicalFileCleanupPath);
                }


                // THIS IS THE CRUCIAL CHANGE: Expose everything to see what is failing
                response.Success = false;
                response.Message = $"CRITICAL ERROR: {ex.Message} -> INNER: {ex.InnerException?.Message} -> STACK TRACE: {ex.StackTrace}";
                return response;
            }

        }



        public async Task<PaginatedListViewModel<PartyViewModel>> AllPartyAsync(int pageNumber = 1, int pageSize = 10)
        {
            // 1. Sanitize inputs
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            int skip = (pageNumber - 1) * pageSize;

            // 2. Define Cache Keys
            string cacheKeyCount = "Allparty_party_count";
            string cacheKeyData = $"ref_All_Party_{pageNumber}_{pageSize}";

            // 3. Get total count (Cached)
            if (!_cache.TryGetValue(cacheKeyCount, out int totalCount))
            {
                totalCount = await _context.Party.CountAsync();
                _cache.Set(cacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
            }

            // 4. Get paginated data (Cached)
            if (!_cache.TryGetValue(cacheKeyData, out List<PartyViewModel>? parties))
            {
                // Pagination happens at the DATABASE level using Skip and Take
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

            // 5. Return view model
            return new PaginatedListViewModel<PartyViewModel>
            {
                TotalItems = totalCount,
                Items = parties ?? Enumerable.Empty<PartyViewModel>(),
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<PaginatedListViewModel<PartyViewModel>> AllSoftDeleteAsync(int PageNumber = 1, int PageSize = 10)
        {
            // 1. Sanitize pagination bounds safely
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);
            int skip = (PageNumber - 1) * PageSize;

            // 2. Define unique cache keys specifically isolated for deleted items
            string CacheKeyItems = $"deleted_party_{PageNumber}_{PageSize}";
            string CacheKeyCount = $"deleted_party_count";

            // Build the query skeleton (does not execute against DB yet)
            var queryDb = _context.Party.AsNoTracking().Where(m => m.IsDeleted);

            // 3. Cache or fetch total row count cleanly using asynchronous execution
            if (!_cache.TryGetValue(CacheKeyCount, out int totalCount))
            {
                totalCount = await queryDb.CountAsync();
                _cache.Set(CacheKeyCount, totalCount, TimeSpan.FromMinutes(5));
            }

            // 4. Cache or slice pagination directly inside database engine
            if (!_cache.TryGetValue(CacheKeyItems, out List<PartyViewModel>? deletedParties))
            {
                deletedParties = await queryDb
                    .OrderBy(m => m.Name).Where(m=>m.IsDeleted==true)
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

            // 5. Build and return model layer
            return new PaginatedListViewModel<PartyViewModel>
            {
                TotalItems = totalCount,
                Items = deletedParties ?? new List<PartyViewModel>(),
                PageNumber = PageNumber,
                PageSize = PageSize
            };
        }

        public async Task<ServiceResponse<string>> EditPartyAsync(EditPartyViewModel model, string Id)
        {

            var response = new ServiceResponse<string>();
            //Create Authorize first cuz only SuperAdmin or Official can Create party
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
            //Checked if Paarty Exists;
            var cleanedtrim = model.Name?.Trim();
            var CheckExistingParty = await _context.Party.FirstOrDefaultAsync(m => m.Id == model.Id && m.Name == cleanedtrim);
            if (CheckExistingParty==null)
            {
                response.Success = false;
                response.Message = "Party Exist Try Another Name";
                return response;
            }

            if(model.LogoFile !=null || model.LogoFile?.Length>0)
            {
                string AllocatedFileName = await _fileService.RegisterAndQueueUploadAsync(file:model.LogoFile, fileType: Enums.FileType.Image, uploadFolder: "Party_Logo", cancellationToken:CancellationToken.None );

                string OldFilePath= CheckExistingParty.LogoUrl??"";
                CheckExistingParty.LogoUrl = $"/Party_Logo/{AllocatedFileName}";
               if(!string.IsNullOrEmpty(CheckExistingParty.LogoUrl)&& CheckExistingParty.LogoUrl.Contains("default-Party_Logo.png")&& !CheckExistingParty.LogoUrl.Contains("default.png"))     
                {
                    string relativePath = OldFilePath.TrimStart('/');

                    string oldFilePath = Path.Combine(_env.WebRootPath, relativePath);

                    // Delete the old file from storage
                    _fileService.DeleteFile(oldFilePath);

                }
            }
            CheckExistingParty.Name = model.Name;
            CheckExistingParty.Description = model.Description;
            
            await _context.Party.AddAsync(CheckExistingParty);
            await _context.SaveChangesAsync();
            response.Success = true;
            response.Message = "Party Updated Succesfully";
            return response;



        }



        public async Task<ServiceResponse<bool>> SoftDeletePartyAsync(string Id, Guid PartyID)
        {
            var response = new ServiceResponse<bool>();
            //Create Authorize first cuz only SuperAdmin or Official can Create party
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
            //Checked if Paarty Exists;
          
            var CheckExistingParty = await _context.Party.FirstOrDefaultAsync(m => m.Id == PartyID);
            if (CheckExistingParty == null)
            {
                response.Success = false;
                response.Message = "Party Exist Try Another Name";
                return response;
            }
            CheckExistingParty.IsDeleted = true;
            CheckExistingParty.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            response.Success= true;
            return response;

        }
        public async Task<ServiceResponse<string>> RestoreDeletedParty(string Id,  Guid PartyID)
        {
            var response = new ServiceResponse<string>();
            //Create Authorize first cuz only SuperAdmin or Official can Create party
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
            //Checked if Paarty Exists;

            var CheckExistingParty = await _context.Party.FirstOrDefaultAsync(m => m.Id == PartyID);
            if (CheckExistingParty == null)
            {
                response.Success = false;
                response.Message = "Party Exist Try Another Name";
                return response;
            }
            var deletedAt= CheckExistingParty.DeletedAt ?? DateTime.UtcNow;
            var DaysSinceDeleted= (DateTime.UtcNow - deletedAt).TotalDays;

            if(!CheckExistingParty.DeletedAt.HasValue)
            {
                response.Success = false;
                response.Message = "Party was never deleted";
                return response;
            }

            if(DaysSinceDeleted>30)
            {
                response.Success = false;
                response.Message = "Limit Exceeded: Cannot restore after 30 days";
                return response;
            }
            CheckExistingParty.IsDeleted = false;
            CheckExistingParty.DeletedAt = null;

            await _context.SaveChangesAsync();
            response.Success = true;
            response.Message = "Product has been restored successfully";
            return response;

        }
    }
}
