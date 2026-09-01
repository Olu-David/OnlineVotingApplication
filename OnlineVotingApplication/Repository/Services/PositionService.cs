using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Areas.Identity.Data;

namespace OnlineVotingApplication.Repository.Services
{
    public class PositionService : iPositionService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMemoryCache _cache;
        private readonly ITenantProvider _tenantProvider; // Added Tenant Provider

        public PositionService(
            AppDbContext context,
            IHttpContextAccessor contextAccessor,
            UserManager<ApplicationUser> userManager,
            IMemoryCache cache,
            ITenantProvider tenantProvider)
        {
            _context = context;
            _contextAccessor = contextAccessor;
            _userManager = userManager;
            _cache = cache;
            _tenantProvider = tenantProvider;
        }

        public async Task<PaginatedListViewModel<PositionDTO>> GetAllPositionsAsync(string electionId, int pageNumber = 1, int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);

            if (!Guid.TryParse(electionId, out Guid electionGuid))
            {
                return new PaginatedListViewModel<PositionDTO>();
            }

            // Optional Tenant Verification check for extra safety
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = _contextAccessor.HttpContext?.User.IsInRole("SuperAdmin") ?? false;

            var baseQuery = _context.Position
                .AsNoTracking()
                .Include(p => p.ElectionEvent)
                .Where(p => p.ElectionEventId == electionGuid && !p.IsDeleted);

            if (!isSuperAdmin)
            {
                baseQuery = baseQuery.Where(p => p.ElectionEvent != null && p.ElectionEvent.TenantId == activeTenantId);
            }

            // 1. Get the total count from DB strictly for this election & tenant
            int totalItems = await baseQuery.CountAsync();

            string cacheKey = $"ref:Positions_Election_{electionGuid}_Tenant_{activeTenantId}_Page_{pageNumber}_Size_{pageSize}";

            if (!_cache.TryGetValue(cacheKey, out List<PositionDTO>? positions))
            {
                // 2. Fetch only the requested page slice
                positions = await baseQuery
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(m => new PositionDTO
                    {
                        Id = m.Id,
                        Name = m.Name,
                        ElectionId = m.ElectionEventId
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
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = _contextAccessor.HttpContext?.User.IsInRole("SuperAdmin") ?? false;

            var query = _context.Position
                .Include(p => p.ElectionEvent)
                .Where(m => m.Id == id && !m.IsDeleted);

            if (!isSuperAdmin)
            {
                query = query.Where(m => m.ElectionEvent != null && m.ElectionEvent.TenantId == activeTenantId);
            }

            var position = await query.FirstOrDefaultAsync();
            return position != null;
        }

        public async Task<ServiceResponse<string>> CreatePositionAsync(PositionDTO model, string userId, Guid electionId)
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

                // Verify target election belongs to active tenant if not SuperAdmin
                if (!isAdmin)
                {
                    Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
                    var electionExists = await _context.ElectionEvents
                        .AnyAsync(e => e.Id == electionId && e.TenantId == activeTenantId);

                    if (!electionExists)
                    {
                        response.Success = false;
                        response.Message = "Unauthorized or invalid election target for your organization.";
                        return response;
                    }
                }

                var positionName = model.Name?.Trim().ToUpper();

                // Check uniqueness strictly within this specific election
                var exists = await _context.Position
                    .AnyAsync(m => m.ElectionEventId == electionId && m.Name == positionName && !m.IsDeleted);

                if (exists)
                {
                    response.Success = false;
                    response.Message = "Position already exists for this election event.";
                    return response;
                }

                var newPosition = new Positions
                {
                    Id = Guid.NewGuid(),
                    Name = positionName ?? string.Empty,
                    IsDeleted = false,
                    ElectionEventId = electionId
                };

                _context.Position.Add(newPosition);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _cache.Remove($"ref:Positions_Election_{electionId}");

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

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            var query = _context.Position
                .Include(p => p.ElectionEvent)
                .Where(m => m.Id == ID);

            if (!isAdmin)
            {
                query = query.Where(m => m.ElectionEvent != null && m.ElectionEvent.TenantId == activeTenantId);
            }

            var existingPosition = await query.FirstOrDefaultAsync(cancellationToken);
            if (existingPosition == null)
            {
                response.Message = "Position does not exist or unauthorized access.";
                response.Success = false;
                return response;
            }

            existingPosition.IsDeleted = true;
            await _context.SaveChangesAsync(cancellationToken);

            response.Message = "Position moved to trash successfully.";
            response.Success = true;
            return response;
        }

        public async Task<ServiceResponse<string>> UpdatePosition(EditPositionModel model, string userId)
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

            if (!Guid.TryParse(model.Id, out Guid parsedPositionId))
            {
                response.Success = false;
                response.Message = "Invalid position ID format.";
                return response;
            }

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            var query = _context.Position
                .Include(p => p.ElectionEvent)
                .Where(m => m.Id == parsedPositionId);

            if (!isAdmin)
            {
                query = query.Where(m => m.ElectionEvent != null && m.ElectionEvent.TenantId == activeTenantId);
            }

            var existingPosition = await query.FirstOrDefaultAsync();
            if (existingPosition == null)
            {
                response.Message = "Position does not exist or unauthorized access.";
                response.Success = false;
                return response;
            }

            existingPosition.Name = model.Name?.Trim().ToUpper() ?? existingPosition.Name;
            await _context.SaveChangesAsync();

            response.Message = "Position updated successfully";
            response.Success = true;
            return response;
        }

        public async Task<PaginatedListViewModel<PositionDTO>> AllSoftDeleted(int PageNumber = 1, int PageSize = 10)
        {
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);
            int skip = (PageNumber - 1) * PageSize;

            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();
            bool isSuperAdmin = _contextAccessor.HttpContext?.User.IsInRole("SuperAdmin") ?? false;

            var queryDb = _context.Position
                .Include(p => p.ElectionEvent)
                .Where(m => m.IsDeleted);

            if (!isSuperAdmin)
            {
                queryDb = queryDb.Where(m => m.ElectionEvent != null && m.ElectionEvent.TenantId == activeTenantId);
            }

            int counted = await queryDb.CountAsync();

            string cacheKey = $"ref_All_SoftDelete_Position_Tenant_{activeTenantId}_{PageNumber}_{PageSize}";
            if (!_cache.TryGetValue(cacheKey, out List<PositionDTO>? pos))
            {
                pos = await queryDb
                    .OrderBy(m => m.Name)
                    .Skip(skip)
                    .Take(PageSize)
                    .Select(m => new PositionDTO
                    {
                        Id = m.Id,
                        Name = m.Name,
                        ElectionId = m.ElectionEventId
                    })
                    .ToListAsync();

                _cache.Set(cacheKey, pos, TimeSpan.FromMinutes(5));
            }

            return new PaginatedListViewModel<PositionDTO>
            {
                Items = pos ?? new List<PositionDTO>(),
                PageNumber = PageNumber,
                PageSize = PageSize,
                TotalItems = counted
            };
        }
    }
}