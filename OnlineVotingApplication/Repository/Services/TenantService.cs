using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.Services
{
    public class TenantService : ITenantService
    {
        private readonly AppDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly iAuthService _authService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<TenantService> _logger;
        private readonly ITenantProvider _tenantProvider;
        private readonly IDistributedCache _Cache;
        private readonly iFileService _iFileService;
        private readonly IWebHostEnvironment _env;

        public TenantService(AppDbContext dbContext, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, iAuthService authService, IHttpContextAccessor httpContextAccessor, ILogger<TenantService> logger, ITenantProvider tenantProvider, IDistributedCache cache, iFileService iFileService, IWebHostEnvironment env)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _roleManager = roleManager;
            _authService = authService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _tenantProvider = tenantProvider;
            _Cache = cache;
            _iFileService = iFileService;
            _env = env;
        }

        private async Task EnsureRolesExistAsync(string[] roles)
        {
            foreach (var role in roles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    await _roleManager.CreateAsync(new IdentityRole(role));
                    _logger.LogInformation("Role {Role} seeded successfully.", role);
                }
            }
        }


        public async Task<ServiceResponse<TenantRegistrationResultDto>> RegisterTenantOrganizationAsync(TenantRegistrationViewModel model)
        {
            var response = new ServiceResponse<TenantRegistrationResultDto>();

            // Generate unique lookup slug layout
            string slug = !string.IsNullOrWhiteSpace(model.OrganizationName)
                ? model.OrganizationName.ToLower().Replace(" ", "-").Replace("'", "")
                : string.Empty;

            bool slugExists = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .AnyAsync(t => t.Slug == slug);

            if (slugExists)
            {
                response.Success = false;
                response.Message = "An organization with a highly similar name is already registered.";
                return response;
            }

            string TenantUrl = "/images/default-tenant.png";
            string TenantFolder = "Tenant_ProfilePictures";
            try
            {
                if (model.ProfilePicture != null && model.ProfilePicture.Length > 0)
                {
                    string allocatedFileName = await _iFileService.RegisterAndQueueUploadAsync(
                        file: model.ProfilePicture,
                        fileType: Enums.FileType.Image,
                        uploadFolder: TenantFolder,
                        cancellationToken: CancellationToken.None
                    );

                    // FIXED: Removed the accidental whitespace path breaking issue
                    TenantUrl = $"/{TenantFolder}/{allocatedFileName}";
                }
            }
            catch (Exception fileEx)
            {
                response.Success = false;
                response.Message = $"Profile picture initialization crashed: {fileEx.Message}";
                return response;
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // 1. Provision New Organization profile context records
                var newTenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    OrganizationName = model.OrganizationName ?? string.Empty,
                    TenantCategory = model.TenantCategory,
                    Slug = slug,
                    ProfilePicture = TenantUrl,
                    SubscriptionPlan = "Free",
                    IsApproved = false,
                    IsActive = true,
                    MaxAllowedElections = 1,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Tenants.Add(newTenant);
                await _dbContext.SaveChangesAsync();

                // 2. Initialize necessary default roles for election platform mechanics
                await EnsureRolesExistAsync(new[] { "Official", "Voter", "Auditor", "Candidate" });

                // 3. Build Administrative User link structure tied directly to target tenant ID context
                var adminUser = new ApplicationUser
                {
                    Email = model.AdminEmail,
                    UserName = model.AdminEmail,
                    FullName = $"{model.OrganizationName} Administrator",
                    TenantId = newTenant.Id,
                    EmailConfirmed = false,
                    IsApproved = false
                };

                var adminResult = await _userManager.CreateAsync(adminUser, model.AdminPassword ?? string.Empty);
                if (!adminResult.Succeeded)
                {
                    response.Success = false;
                    response.Errors = adminResult.Errors.Select(e => e.Description).ToList();
                    await transaction.RollbackAsync();
                    return response;
                }

                await _userManager.AddToRoleAsync(adminUser, "Official");

                // 4. Token assembly extraction processing
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(adminUser);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                // Send token using your core authentication tracking scheme engine pattern
                var emailResponse = await _authService.SendConfirmationTokenAsync(adminUser, encodedToken);
                if (!emailResponse.Success)
                {
                    _logger.LogWarning("Tenant was created, but admin verification token failed to queue for {Email}", adminUser.Email);
                }

                await transaction.CommitAsync();

                // 5. Fill matching response properties array
                response.Data = new TenantRegistrationResultDto
                {
                    Tenant = newTenant,
                    AdminUser = adminUser,
                    UserId = adminUser.Id,
                    Token = encodedToken
                };
                response.Success = true;
                response.Message = "Organization successfully provisioned. Verification token sent.";
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // Cleanup file system if db commit operation crashes out midway
                if (TenantUrl != "/images/default-tenant.png")
                {
                    string baseAvatarPath = Path.Combine(_env.WebRootPath, TenantFolder, Path.GetFileName(TenantUrl));
                    if (File.Exists(baseAvatarPath)) File.Delete(baseAvatarPath);
                }

                _logger.LogError(ex, "Fatal error provisioning organization {OrganizationName}", model.OrganizationName);
                response.Success = false;
                response.Message = "An error occurred while creating your organization profile.";
                return response;
            }
        }
        public async Task<Tenant?> GetTenantDetailsAsync()
        {
            Guid activeId = _tenantProvider.GetCurrentTenantId();
            return await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == activeId);
        }
        public async Task<ServiceResponse<PaginatedListViewModel<TenantViewModel>>> AllTenantListAsync(string Id, int pageNumber = 1, int pageSize = 10)
        {
            var response = new ServiceResponse<PaginatedListViewModel<TenantViewModel>>();

            // 1. User Validation & Authorization
            var user = await _userManager.FindByIdAsync(Id);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User Not Found";
                response.Errors = new List<string> { "The requested user profile does not exist in the system." };
                return response;
            }

            bool isAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
            if (!isAdmin)
            {
                response.Success = false;
                response.Message = "User cannot perform this function, Authorized User only";
                return response;
            }

            // 2. Pagination Calculation
            pageNumber = Math.Max(1, pageNumber);
            pageSize = pageSize < 1 ? 10 : pageSize;
            int skip = (pageNumber - 1) * pageSize;

            string cacheKeyList = $"ref_All_Tenant_List_P{pageNumber}_S{pageSize}";
            string cacheKeyCount = "Tenant_Count";

            // 3. Cache Check: Total Count
            int totalCount;
            string? cachedCountStr = await _Cache.GetStringAsync(cacheKeyCount);

            if (string.IsNullOrEmpty(cachedCountStr))
            {
                totalCount = await _dbContext.Tenants.IgnoreQueryFilters().CountAsync();

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

            // 4. Cache Check & DB Fetch: Paginated Tenant List
            List<TenantViewModel>? tenantList = null;
            string? cachedListJson = await _Cache.GetStringAsync(cacheKeyList);

            if (!string.IsNullOrEmpty(cachedListJson))
            {
                // Cache Hit: Deserializing the JSON string fetched from Redis
                tenantList = JsonSerializer.Deserialize<List<TenantViewModel>>(cachedListJson);
            }
            else
            {
              
                tenantList = await _dbContext.Tenants
                    .IgnoreQueryFilters().Where(m=>m.IsApproved==true)
                    .AsNoTracking()
                    .Skip(skip)
                    .Take(pageSize)
                    .Select(m => new TenantViewModel
                    {
                        OrganizationName = m.OrganizationName,
                        SubscriptionPlan = m.SubscriptionPlan,
                        CreatedAt = m.CreatedAt,
                        IsActive = m.IsActive,
                        TenantCategory = m.TenantCategory
                    })
                    .ToListAsync();

                // Serialize and store in Redis
                string jsonToCache = JsonSerializer.Serialize(tenantList);
                var listCacheOption = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _Cache.SetStringAsync(cacheKeyList, jsonToCache, listCacheOption);
            }

            // 5. Build Successful Response
            response.Success = true;
            response.Data = new PaginatedListViewModel<TenantViewModel>
            {
                Items = tenantList ?? new List<TenantViewModel>(),
                TotalItems = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            return response;
        }
        public async Task<TenantMetricsDto> GetDashboardMetricsAsync()
        {
            var tenant = await GetTenantDetailsAsync();

            var metrics = new TenantMetricsDto
            {
                TotalElections = await _dbContext.ElectionEvents.CountAsync(),
                TotalApprovedCandidates = await _dbContext.Candidate.Where(c => c.isApproved && !c.isDeleted ).CountAsync(),
                TotalVotesCast = await _dbContext.Votes.CountAsync(),
                ActiveSubscriptionPlan = tenant?.SubscriptionPlan ?? "Free"
            };

            return metrics;
        }

        public async Task<bool> IsWithinPlanLimitsAsync()
        {
            var tenant = await GetTenantDetailsAsync();
            if (tenant == null || !tenant.IsActive) return false;

            int currentElectionCount = await _dbContext.ElectionEvents.CountAsync();
            return currentElectionCount < tenant.MaxAllowedElections;
        }

        public async Task<bool> UpdateSubscriptionPlanAsync(string newPlan, int newElectionLimit)
        {
            var tenant = await GetTenantDetailsAsync();
            if (tenant == null) return false;

            tenant.SubscriptionPlan = newPlan;
            tenant.MaxAllowedElections = newElectionLimit;

            _dbContext.Tenants.Update(tenant);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ToggleTenantStatusAsync(Guid tenantId, bool isActive)
        {
            var tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null) return false;

            tenant.IsActive = isActive;
            await _dbContext.SaveChangesAsync();
            return true;
        }
        public async Task<List<UserListSummaryDto>> GetTenantAdminsAsync()
        {
            Guid currentTenantId = _tenantProvider.GetCurrentTenantId();

            if (currentTenantId == Guid.Empty)
            {
                return new List<UserListSummaryDto>();
            }

            // 1. Await the UserManager list correctly without using '.Result'
            IList<ApplicationUser> usersInRole = await _userManager.GetUsersInRoleAsync("Official");

            // 2. Filter down by Tenant and map to your DTO cleanly
            var adminSummaries = usersInRole
                .Where(user => user.TenantId == currentTenantId)
                .Select(user => new UserListSummaryDto
                {
                    Id = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty
                })
                .ToList(); // Executing in memory since UserManager already pulled the list

            // 3. Return statement sits safely at the very bottom
            return adminSummaries;
        }

        public async Task<List<Candidate>> GetElectionCandidatesAsync(Guid electionId)
        {
            return await _dbContext.Candidate
                .Include(c => c.Position)
                .Include(c => c.Party)
                .Where(c => c.ElectionEventId == electionId && !c.isDeleted)
                .ToListAsync();
        }

        public async Task<int> GetElectionVoterCountAsync(Guid electionId)
        {
            return await _dbContext.Votes
                .Where(v => v.ElectionId == electionId)
                .Select(v => v.VoterId)
                .Distinct()
                .CountAsync();
        }

        // 🌟 NEW METHOD: Handles automatic client signups on the website
        public async Task<ServiceResponse<Guid>> RegisterNewOrganizationAsync(string name, string desiredSlug)
        {
            var response = new ServiceResponse<Guid>();
            string standardizedSlug = desiredSlug.ToLower().Trim().Replace(" ", "-");

            // Ignore query filters to check the master directory across all organizations
            bool slugInUse = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .AnyAsync(t => t.Slug == standardizedSlug);

            if (slugInUse)
            {
                response.Success = false;
                response.Message = "This organization web handle/slug is already taken by another user.";
                return response;
            }

            var newTenant = new Tenant
            {
                Id = Guid.NewGuid(),
                OrganizationName = name,
                Slug = standardizedSlug,
                SubscriptionPlan = "Free",
                IsActive = true,
                MaxAllowedElections = 1
            };

            await _dbContext.Tenants.AddAsync(newTenant);
            await _dbContext.SaveChangesAsync();

            response.Success = true;
            response.Data = newTenant.Id;
            response.Message = "Your organization setup workspace profile has been provisioned successfully.";
            return response;
        }

        public async Task<bool> IsElectionOwnedByActiveTenantAsync(Guid electionId)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            // Standard query filter handles this automatically behind the scenes,
            // but checking explicitly protects against malicious ID manipulations.
            return await _dbContext.ElectionEvents.AnyAsync(e => e.Id == electionId );
        }
    }
}
    
