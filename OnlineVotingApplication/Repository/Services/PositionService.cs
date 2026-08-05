using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Build.Tasks;
using OnlineVotingApplication.Areas.Identity.Data;
using Microsoft.Diagnostics.Runtime.AbstractDac;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace OnlineVotingApplication.Repository.Services
{
    public class PositionService : iPositionService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMemoryCache _cache;

        public PositionService(AppDbContext context, IHttpContextAccessor contextAccessor, UserManager<ApplicationUser> userManager, IMemoryCache cache)
        {
            _context = context;
            _contextAccessor = contextAccessor;
            _userManager = userManager;
            _cache = cache;
        }

        public async Task<PaginatedListViewModel<PositionDTO>> GetAllPositionsAsync(string id, int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);

            // 1. Get the total count from DB
            int totalItems = await _context.Position.AsNoTracking().CountAsync();

            string cacheKey = $"ref:Positions_Page_{pageNumber}_Size_{pageSize}";

            if (!_cache.TryGetValue(cacheKey, out List<PositionDTO>? positions))
            {
                // 2. Fetch only the requested page slice
                positions = await _context.Position
                    .AsNoTracking()
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(m => new PositionDTO
                    {
                        Id = m.Id,
                        Name = m.Name
                    })
                    .ToListAsync();

                _cache.Set(cacheKey, positions, TimeSpan.FromMinutes(30));
            }

            // 3. Return the unified model
            return new PaginatedListViewModel<PositionDTO>
            {
                Items = positions ?? new List<PositionDTO>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };
        }




        public async Task<bool> GetPositionByIdAsync(Guid id)
        {
            var position = await _context.Position.FirstOrDefaultAsync(m => m.Id == id);
            if (position == null)
            {
                return false;
            }
            return true;
        }

        public async Task<ServiceResponse<string>> CreatePositionAsync(PositionDTO model, string userId)
        {
            var response = new ServiceResponse<string>();

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var user = await _userManager.FindByIdAsync(userId);

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

                var positionName = model.Name?.Trim().ToUpper();

                var exists = await _context.Position
                    .AnyAsync(m => m.Name == positionName && !m.IsDeleted);

                if (exists)
                {
                    response.Success = false;
                    response.Message = "Position already exists for this election";
                    return response;
                }

              
                var newPosition = new Positions
                {
                    Name = positionName,
                    IsDeleted = false
                };

                _context.Position.Add(newPosition);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                //  Clear the positions list cache so changes are immediately visible
                _cache.Remove("ref_ALL-Positions");

                response.Success = true;
                response.Message = "Position created successfully";

                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                response.Success = false;
                response.Message = "An unexpected error occurred during database save operation.";
                response.Errors = new List<string> { ex.Message, ex.InnerException?.Message ?? "" };

                return response;
            }
        }

        public async Task<ServiceResponse<string>> DeletePosition(Guid ID, string userId, CancellationToken cancellationToken = default)
        {
            var response = new ServiceResponse<string>();
            var user = await _userManager.FindByIdAsync(userId);

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

            // Passed the token to protect the position lookup query
            var existingPositon = await _context.Position.FirstOrDefaultAsync(m => m.Id == ID, cancellationToken);
            if (existingPositon == null)
            {
                response.Message = "Position does not Exist";
                response.Success = false;
                return response;
            }

            existingPositon.IsDeleted = true;
            existingPositon.DeletedAt = DateTime.UtcNow;

            // Passed the token to protect the save operation from hanging during a server shutdown/abort
            await _context.SaveChangesAsync(cancellationToken);

            response.Message = "Position moved to trash. It will be permanently deleted in 30 days.";
            response.Success = true;
            return response;
        }

        public async Task<ServiceResponse<string>> UpdatePosition(EditPositionModel model, string ID)
        {
            var response = new ServiceResponse<string>();
            var user = await _userManager.FindByIdAsync(ID);

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

            var existingPositon = await _context.Position.FirstOrDefaultAsync(m => m.Id == model.Id);
            if (existingPositon == null)
            {
                response.Message = "Position does not Exist";
                response.Success = false;
                return response;
            }

            existingPositon.Name = model.Name;

            await _context.SaveChangesAsync();


            response.Message = "Positon Deleted Successfully";
            response.Success = true;
            return response;
        }

        public async Task<PaginatedListViewModel<PositionDTO>> AllSoftDeleted(int PageNumber = 1, int PageSize = 10)
        {
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);

            var QueryDb = _context.Position.Where(m => m.IsDeleted);
            int counted = await QueryDb.CountAsync();

            string CacheKey = $"ref_All_SoftDelete_Position_{PageNumber}_{PageSize}";
            if (!_cache.TryGetValue(CacheKey, out List<PositionDTO>? Pos))
            {

                Pos = await QueryDb.OrderBy(m => m.Name).Select(m => new PositionDTO
                {
                    Name = m.Name


                }).ToListAsync();

                _cache.Set(CacheKey, Pos, TimeSpan.FromMinutes(5));
            }

            return new PaginatedListViewModel<PositionDTO>
            {
                Items = Pos ?? new List<PositionDTO>(),
                PageNumber = PageNumber,
                PageSize = PageSize,
                TotalItems = counted
            };
        }
    }
}

