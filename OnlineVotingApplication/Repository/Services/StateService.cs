using Azure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Areas.Identity.Data;

namespace OnlineVotingApplication.Repository.Services
{
    public class StateService:iStateService
    {

       
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IMemoryCache _cache;
        private readonly ILogger<StateService> _logger;

        public StateService(AppDbContext context, IHttpContextAccessor contextAccessor, IMemoryCache cache, ILogger<StateService> logger)
        {
            _context = context;
            _contextAccessor = contextAccessor;
            _cache = cache;
            _logger = logger;
        }

        public async Task<bool> CreateStateAsync(StateDTO state)
        {
            var transaction = await _context.Database.BeginTransactionAsync();
            try
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

                var newState = new States
                {
                    Id = state.Id,
                    Name = state.Name ?? ""
                };
                // 4. Create state
                _context.States.Add(newState);
                await _context.SaveChangesAsync();

               await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                 _logger.LogError(ex.Message);
                return false;
            }

        }

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

            public async Task<States?> GetStateByIdAsync(Guid id)
            {
                return await _context.States.FirstOrDefaultAsync(m=>m.Id==id);
            }

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

            var state =await _context.States.FirstOrDefaultAsync(m => m.Id == id);
            if(state==null)
            {
                return false;
            }
            _context.States.Remove(state);
                await _context.SaveChangesAsync();
                return true;
            }
        }
    }
