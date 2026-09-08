using Azure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;

namespace OnlineVotingApplication.Repository.Services
{
    public class StateService : iStateService
    {


        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IMemoryCache _cache;
        private readonly ILogger<StateService> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        #region StateService
        public StateService(AppDbContext context, IHttpContextAccessor contextAccessor, IMemoryCache cache, ILogger<StateService> logger, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _contextAccessor = contextAccessor;
            _cache = cache;
            _logger = logger;
            _userManager = userManager;
        }
        #endregion

        #region CreateStateAsync
        public async Task<bool> CreateStateAsync(StateDTO state, string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            bool isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _userManager.IsInRoleAsync(user, "Official");
            if (!isSuperAdmin && !isOfficial)
            {
                return false;
            }

            var cleanedName = state.Name?.Trim();
            if (string.IsNullOrEmpty(cleanedName))
            {
                return false;
            }

            // Check if the state already exists to avoid unique constraint violations
            var stateExists = await _context.States
                .AsNoTracking()
                .AnyAsync(m => m.Name.ToLower() == cleanedName.ToLower());

            if (stateExists)
            {
                return false;
            }

            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var newState = new States
                    {
                        Id = state.Id ?? Guid.NewGuid(),
                        Name = cleanedName
                    };

                    await _context.States.AddAsync(newState);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error creating state '{StateName}' for user {UserId}", cleanedName, userId);
                    return false;
                }
            });
        }
        #endregion


        #region UpdateStateAsync
        public async Task<bool> UpdateStateAsync(UpdateStateDto model, string Id)
        {
            //Check Authentication
            var user = await _userManager.FindByEmailAsync(Id);
            if (user == null)
            {
                return false;
            }
            bool IsSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            bool IsOfficial = await _userManager.IsInRoleAsync(user, "Official");
            if (!IsSuperAdmin && !IsOfficial)
            {
                return false;
            }

            var findState = await _context.States.FirstOrDefaultAsync(m => m.Id == model.Id);

            if (findState == null)
            {
                return false;
            }
            //Send data for editing


            findState.Name = model.Name ?? "";
            _context.Update(findState);
            await _context.SaveChangesAsync();
            return true;

        }
        #endregion
        #region GetAllStatesAsync
        public async Task<List<StateDTO>> GetAllStatesAsync()
        {
            string cachekey = "ref_ALL-State";
            if (!_cache.TryGetValue(cachekey, out List<StateDTO>? state))
            {
                state = await _context.States.Select(m => new StateDTO
                {
                    Id = m.Id,
                    Name = m.Name.TrimStart().ToUpper()
                }).ToListAsync();

                _cache.Set(cachekey, state, TimeSpan.FromMinutes(30));

            }
            return state!;
        }
        #endregion

        #region GetStateByIdAsync
        public async Task<States?> GetStateByIdAsync(Guid id)
        {
            return await _context.States.FirstOrDefaultAsync(m => m.Id == id);
        }
        #endregion

        #region DeleteStateAsync
        public async Task<bool> DeleteStateAsync(Guid id)
        {
            var user = _contextAccessor?.HttpContext?.User;

            // 1. Check authentication
            if (user?.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            // 2. Check roles
            bool isAdmin = user.IsInRole("SuperAdmin");
            bool isOfficial = user.IsInRole("Official");

            // 3. Block if NOT authorized
            if (!isAdmin && !isOfficial)
            {
                return false!;
            }

            var state = await _context.States.FirstOrDefaultAsync(m => m.Id == id);
            if (state == null)
            {
                return false;
            }
            _context.States.Remove(state);
            await _context.SaveChangesAsync();
            return true;
        }
        #endregion
    }
}