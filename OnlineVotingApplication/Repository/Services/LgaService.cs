using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Jobs;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Threading;
using System.Threading.Channels;

namespace OnlineVotingApplication.Repository.Services
{
    public class LgaService : iLgaService
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMemoryCache _cache;
        private readonly ILogger<LgaService> _logger;
        #region LgaService
        public LgaService(AppDbContext context, UserManager<ApplicationUser> userManager, IMemoryCache cache, ILogger<LgaService> logger)
        {
            _context = context;
            _userManager = userManager;
            _cache = cache;
            _logger = logger;
        }
        #endregion

        #region CreateLgaAsync
        public async Task<ServiceResponse<string>> CreateLgaAsync(LgaDTO lga, string Id)    
        {
     var response = new ServiceResponse<string>();

    // 1. Validate inputs early before hitting database strategy/transactions
        var user = await _userManager.FindByIdAsync(Id);
        if (user == null)
    {
        response.Success = false;
        response.Message = "User does not exist";
        return response;
    }

    bool isAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
    if (!isAdmin)
    {
        response.Success = false;
        response.Message = "Only Authorized user have Access to this feature";
        return response;
    }

    // Check for duplicate LGA name within the same State
    var lgaExists = await _context.Lgas
        .AsNoTracking()
        .AnyAsync(m => m.StateId == lga.StateId && m.Name.ToLower() == lga.Name.ToLower());

    if (lgaExists)
    {
        response.Success = false;
        response.Message = "LGA exists Already, try Another one";
        return response;
    }

    var strategy = _context.Database.CreateExecutionStrategy();

    return await strategy.ExecuteAsync(async () =>
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var newLGA = new LGA
            {
                Id = lga.Id == Guid.Empty ? Guid.NewGuid() : lga.Id,
                Name = lga.Name,
                StateId = lga.StateId
            };

            await _context.Lgas.AddAsync(newLGA);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            response.Success = true;
            response.Message = "LGA Saved Successfully to the database";
            return response;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            _logger.LogError(ex, "Fatal error inside CreateLgaAsync for user {UserId}", Id);

            response.Success = false;
            response.Message = "An Unexpected Error Occured";
            response.Errors = new List<string>
            {
                ex.Message
            };
            return response;
        }
    });
}
#endregion

        #region GetAllLgasAsync
        public async Task<PaginatedListViewModel<LgaDTO>> GetAllLgasAsync(int PageNumber = 1, int PageSize = 10)
        {
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);


            var cacheKey = "all_lgas";
            var queryDb = await _context.Lgas.AsNoTracking().ToListAsync();
            int ItemsCount = queryDb.Count;

            if (!_cache.TryGetValue(cacheKey, out List<LgaDTO>? lgas))
            {
                lgas = queryDb.Skip((PageNumber - 1) * PageSize).Take(PageSize)
                    .Select(l => new LgaDTO
                    {
                        Id = l.Id,
                        Name = l.Name,
                        StateId = l.StateId
                    }).ToList();


                _cache.Set(cacheKey, lgas, TimeSpan.FromMinutes(30));
            }

            return new PaginatedListViewModel<LgaDTO>()
            {
                Items = lgas != null ? lgas : Enumerable.Empty<LgaDTO>(),
                PageNumber = PageNumber,
                PageSize = PageSize,
                TotalItems = ItemsCount

            };
        }
        #endregion
        #region GetLgasByStateIdAsync
        public async Task<PaginatedListViewModel<LgaDTO>> GetLgasByStateIdAsync(Guid stateId, int PageNumber = 1, int PageSize = 10)
        {
            // 1. Sanitize input variables safely
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);
            int skip = (PageNumber - 1) * PageSize;

            // 2. Define unique cache keys for both items and total count
            string CacheKeyItems = $"lgas_state_{stateId}_{PageNumber}_{PageSize}";
            string CacheKeyCount = $"lgas_state_{stateId}_count";

            var queryDb = _context.Lgas.AsNoTracking().Where(x => x.StateId == stateId);

            // 3. Try to get total count from cache, or fetch asynchronously from DB
            if (!_cache.TryGetValue(CacheKeyCount, out int totalCount))
            {
                totalCount = await queryDb.CountAsync();
                _cache.Set(CacheKeyCount, totalCount, TimeSpan.FromMinutes(10));
            }

            // 4. Try to get paginated list from cache, or slice explicitly with Skip/Take from DB
            if (!_cache.TryGetValue(CacheKeyItems, out List<LgaDTO>? lgas))
            {
                lgas = await queryDb
                    .OrderBy(x => x.Name)
                    .Skip(skip)
                    .Take(PageSize)
                    .Select(x => new LgaDTO
                    {
                        Id = x.Id,
                        Name = x.Name,
                        StateId = x.StateId
                    })
                    .ToListAsync();

                _cache.Set(CacheKeyItems, lgas, TimeSpan.FromMinutes(5));
            }

            // 5. Build and return the view model
            return new PaginatedListViewModel<LgaDTO>
            {
                Items = lgas ?? new List<LgaDTO>(),
                TotalItems = totalCount,
                PageSize = PageSize,
                PageNumber = PageNumber
            };
        }
        #endregion

        #region GetLgaByIdAsync
        public async Task<LGA?> GetLgaByIdAsync(Guid id)
        {
            return await _context.Lgas.FirstOrDefaultAsync(m => m.Id == id);
        }
        #endregion

        #region DeleteLgaAsync
        public async Task<ServiceResponse<string>> DeleteLgaAsync(LgaDTO dto, string ID)
        {

            var response = new ServiceResponse<string>();



            var user = await _userManager.FindByIdAsync(ID);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User dose not exist";
                return response;
            }
            bool IsAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            if (!IsAdmin)
            {
                response.Success = false;
                response.Message = "Only Authorized user have Access to this feature";
                return response;

            }
            string cleanedDtoName = dto.Name?.Trim() ?? string.Empty;
            var LGA = await _context.Lgas.FirstOrDefaultAsync(m => m.Id == dto.Id && m.Name == cleanedDtoName);
            if (LGA == null)
            {
                response.Success = false;
                response.Message = "LGA does not exist";
                return response;
            }
            _context.Lgas.Remove(LGA);
            await _context.SaveChangesAsync();


            response.Success = true;
            response.Message = "LGA Saved Succesfully to the database";
            return response;

        }
        #endregion

    }
}






//var users = new List<(ApplicationUser User, string Password, string Role)>
//                {
//                    (new ApplicationUser {
//                        FullName = "Olusanya David Victor",
//                        UserName = "superadmin@election.com", // Changed to Email layout for seamless identity token parsing
//                        Email = "superadmin@election.com",
//                        EmailConfirmed = true,
//                        profileImage = "",
//                        StateId = null
//                    }, "SecureP@ss123!", "SuperAdmin"),

//                    (new ApplicationUser {
//                        FullName = "Election Official",
//                        UserName = "official@election.com",
//                        Email = "official@election.com",
//                        EmailConfirmed = true,
//                        profileImage = "",
//                        StateId = null
//                    }, "SecureP@ss123!", "Official"),

//                    (new ApplicationUser {
//                        FullName = "Voter User",
//                        UserName = "voter@election.com",
//                        Email = "voter@election.com",
//                        EmailConfirmed = true,
//                        profileImage = "",
//                        StateId = null
//                    }, "SecureP@ss123!", "Voter"),

//                    (new ApplicationUser {
//                        FullName = "Auditor User",
//                        UserName = "auditor@election.com",
//                        Email = "auditor@election.com",
//                        EmailConfirmed = true,
//                        profileImage = "",
//                        StateId = null
//                    }, "SecureP@ss123!", "Auditor"),

//                    (new ApplicationUser {
//                        FullName = "Candidate User",
//                        UserName = "candidate@election.com",
//                        Email = "candidate@election.com",
//                        EmailConfirmed = true,
//                        profileImage = "",
//                        StateId = null
//                    }, "SecureP@ss123!", "Candidate")
//                };