using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.DataTransferView;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using Microsoft.AspNetCore.Identity;
using OnlineVotingApplication.Areas.Identity.Data;

namespace OnlineVotingApplication.Repository.Services
{
    public class ElectionService: IElectionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ElectionService> _logger;
        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _Accessor;
        private readonly UserManager<ApplicationUser> _UserManager;

        public ElectionService(AppDbContext context, ILogger<ElectionService> logger, IMemoryCache cache, IHttpContextAccessor accessor, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _logger = logger;
            _cache = cache;
            _Accessor = accessor;
            _UserManager = userManager;
        }

        public async Task<ServiceResponse<ElectionDto>> CreateElectionAsync(ElectionDto model, string UserId)
        {
            var response = new ServiceResponse<ElectionDto>();
            var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                 var user = await _UserManager.FindByIdAsync(UserId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                return response;
            }

            bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");
            

            if (!isAdmin)
            {
                response.Success = false;
                response.Message = "Only authorized users have access to this feature";
                return response;
            }

                if (model == null)
                {
                    response.Success = false;
                    response.Message = "Invalid election data";
                    return response;
                }


                var election = new Election
                {
                    Id = model.Id,
                    IsActive = false

                };

                await _context.Election.AddAsync(election);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                response.Data = model;
                response.Success = true;
                response.Message = "Election created successfully";

                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "An Error Occured Successfully";
                response.Errors = new List<string>
                {
                    ex.Message
                };
                await transaction.RollbackAsync();
                return response;
            }
        }
        public async Task<ServiceResponse<bool>> EndElectionAsync(Guid electionId, string UserId)
        {

            var response = new ServiceResponse<bool>();

            try
            {
                var user = await _UserManager.FindByIdAsync(UserId);

                if (user == null)
                {
                    response.Success = false;
                    response.Message = "User doesn't exist";
                    return response;
                }

                bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");


                if (!isAdmin)
                {
                    response.Success = false;
                    response.Message = "Only authorized users have access to this feature";
                    return response;
                }
                var election = await _context.Election
                .FirstOrDefaultAsync(x => x.Id == electionId);

                if (election == null)
                {
                    response.Success = false;
                    response.Message = "Election not found";
                    return response;
                }

                if (!election.IsActive)
                {
                    response.Success = false;
                    response.Message = "Election is not active";
                    return response;
                }

                election.IsActive = false;
                election.EndDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                response.Success = true;
                response.Data = true;
                response.Message = "Election ended successfully";

                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "An Error Occured Successfully";
                response.Errors = new List<string>
                {
                    ex.Message
                };
              
                return response;
            }
        }



        

        public async Task<ServiceResponse<bool>> StartElectionAsync(Guid electionId, string UserId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                var user = await _UserManager.FindByIdAsync(UserId);

                if (user == null)
                {
                    response.Success = false;
                    response.Message = "User doesn't exist";
                    return response;
                }

                bool isAdmin = await _UserManager.IsInRoleAsync(user, "SuperAdmin");


                if (!isAdmin)
                {
                    response.Success = false;
                    response.Message = "Only authorized users have access to this feature";
                    return response;
                }
                var election = await _context.Election
                .FirstOrDefaultAsync(x => x.Id == electionId);

            if (election == null)
            {
                response.Success = false;
                response.Message = "Election not found";
                return response;
            }

            if (election.IsActive)
            {
                response.Success = false;
                response.Message = "Election already started";
                return response;
            }

            election.IsActive = true;
            election.ElectionYear = DateTime.UtcNow.Year;
            election.StartDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            response.Success = true;
            response.Data = true;
            response.Message = "Election started successfully";

            return response;
        }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "An Error Occured Successfully";
                response.Errors = new List<string>
                {
                    ex.Message
                };

                return response;
            }
        }
    }
}
