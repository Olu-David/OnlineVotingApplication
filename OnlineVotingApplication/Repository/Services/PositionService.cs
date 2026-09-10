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

        #region PositionService
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
        #endregion

        #region GetAllPositionsAsync

        public async Task<PaginatedListViewModel<PositionDTO>> GetAllPositionsAsync(Guid electionId,int pageNumber = 1, int pageSize = 10,CancellationToken cancellationToken = default)
        {
            // ─── STEP 1: Make sure page numbers are valid ───────────────────
            // If someone passes page 0 or a negative number, treat it as page 1.
            // Same for page size – never allow 0 or negative.
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            // ─── STEP 2: Who is asking? ─────────────────────────────────────
            // Get the current tenant ID (Guid.Empty means "no tenant").
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            // Check if the current user is a SuperAdmin.
            bool isSuperAdmin = _contextAccessor.HttpContext?.User.IsInRole("SuperAdmin") ?? false;

            // ─── STEP 3: Quick exit for users with no tenant context ────────
            // If the user is NOT a SuperAdmin AND has no tenant, they shouldn't
            // see any positions. Return an empty result right away (fast, no DB hit).
            if (!isSuperAdmin && activeTenantId == Guid.Empty)
            {
                return new PaginatedListViewModel<PositionDTO>
                {
                    Items = new List<PositionDTO>(),
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = 0
                };
            }

            // ─── STEP 4: Build the base query ───────────────────────────────
            // We want positions that:
            //   • Belong to the given election
            //   • Are NOT soft-deleted
            var query = _context.Position
                .AsNoTracking()   // faster because we won't modify these records
                .Where(p => p.ElectionEventId == electionId && !p.IsDeleted);

            // ─── STEP 5: Add tenant filter for non-SuperAdmins ──────────────
            // A regular tenant user should only see positions whose election
            // belongs to their tenant. SuperAdmins skip this check.
            if (!isSuperAdmin)
            {
                query = query.Where(p => p.ElectionEvent!.TenantId == activeTenantId);
            }

            // ─── STEP 6: Try to get data from cache first ───────────────────
            // Cache is like a quick-access storage. If we've fetched this exact
            // page before, use it instead of hitting the database again.
            string scope = isSuperAdmin ? "super" : $"tenant_{activeTenantId}";
            string cacheKey = $"positions_{electionId}_{scope}_page{pageNumber}_size{pageSize}";

            if (_cache.TryGetValue(cacheKey, out PaginatedListViewModel<PositionDTO>? cachedResult)
                && cachedResult != null)
            {
                return cachedResult;   // cache hit – return immediately
            }

            // ─── STEP 7: If not cached, ask the database ────────────────────
            // First, how many positions are there total? (needed for pagination)
            int totalItems = await query.CountAsync(cancellationToken);

            // Then get only the page we need (skip and take).
            var positions = await query
                .OrderBy(p => p.Name)                            // always order for stable paging
                .Skip((pageNumber - 1) * pageSize)               // skip previous pages
                .Take(pageSize)                                  // take current page
                .Select(p => new PositionDTO
                {
                    Id = p.Id,
                    Name = p.Name,
                    ElectionId = p.ElectionEventId ?? Guid.Empty
                })
                .ToListAsync(cancellationToken);

            // ─── STEP 8: Wrap everything in one result object ───────────────
            var result = new PaginatedListViewModel<PositionDTO>
            {
                Items = positions,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            // ─── STEP 9: Save to cache for next time ────────────────────────
            // Keep it for 30 minutes. After that, it'll be fetched fresh.
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));

            // ─── STEP 10: Return the result ─────────────────────────────────
            return result;
        }

        #endregion

        #region GetPositionByIdAsync
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
        #endregion

        #region CreatePositionAsync
        public async Task<ServiceResponse<string>> CreatePositionAsync(PositionDTO model, string userId, Guid electionId)
        {
            var response = new ServiceResponse<string>();
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
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
            });
        }
        #endregion

        #region DeletePosition
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
        #endregion

        #region UpdatePosition
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
        #endregion

        #region AllSoftDeleted
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
                        ElectionId = m.ElectionEventId??Guid.Empty
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
        #endregion
    }
}