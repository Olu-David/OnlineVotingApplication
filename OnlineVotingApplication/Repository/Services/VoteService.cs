using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Repository.Services
{


    public class VoteService : IVoteService
    {
        private readonly AppDbContext _context;
        private readonly ILogger <VoteService> _logger;












        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _Accessor;
        private readonly UserManager<ApplicationUser> _UserMananager;

        public VoteService(AppDbContext context, ILogger<VoteService> logger, IMemoryCache cache, IHttpContextAccessor accessor, UserManager<ApplicationUser> userMananager)
        {
            _context = context;
            _logger = logger;
            _cache = cache;
            _Accessor = accessor;
            _UserMananager = userMananager;
        }

        public async Task<ServiceResponse<string>> CastVoteAsync(string voterId, Guid candidateId, Guid electionId, Guid positionId, string UserId, string Id)
        {
            var response = new ServiceResponse<string>();
            //1.Check is Voter is Eligible to vote
            var user = await _UserMananager.FindByIdAsync(UserId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User doesn't exist";
                return response;
            }

            bool isAdmin = await _UserMananager.IsInRoleAsync(user, "SuperAdmin");
            bool isOfficial = await _UserMananager.IsInRoleAsync(user, "Official");

            if (!isAdmin && !isOfficial)
            {
                response.Success = false;
                response.Message = "Only authorized users have access to this feature";
                return response;
            }

            var officialUser = await _context.Users.FirstOrDefaultAsync(x => x.OfficialStaffId == Id);

            if (officialUser == null)
            {
                response.Success = false;
                response.Message = "Access denied. Invalid official credentials.";
                return response;
            }
            // 2. Check election
            var election = await _context.Election
                .FirstOrDefaultAsync(e => e.Id == electionId && e.IsActive);

            if (election == null)
            {
                response.Success = false;
                response.Message = "Election is not active";
                return response;
            }

            // 3. Prevent double voting
            var alreadyVoted = await _context.Votes
                .AnyAsync(v => v.VoterId == voterId
                            && v.ElectionId == electionId
                            && v.PositionId == positionId);

            if (alreadyVoted)
            {
                response.Success = false;
                response.Message = "You have already voted for this position";
                return response;
            }

            // 4. Create vote
            var vote = new Vote
            {
                VoterId = voterId,
                CandidateId = candidateId,
                ElectionId = electionId,
                PositionId = positionId,
                VotedAt = DateTime.UtcNow
            };

            _context.Votes.Add(vote);
            await _context.SaveChangesAsync();

            response.Success = true;
            response.Message = "Vote cast successfully";

            return response;
        }


        public async Task<ServiceResponse<List<VoteResultDto>>> GetResultsAsync(Guid electionId, Guid positionId)
        {
            var response = new ServiceResponse<List<VoteResultDto>>();

            try
            {
                var results = await _context.Votes
                    .Where(v => v.ElectionId == electionId && v.PositionId == positionId)
                    .GroupBy(v => new
                    {
                        v.CandidateId,
                        v.Candidate.Name
                    })
                    .Select(g => new VoteResultDto
                    {
                        CandidateId = g.Key.CandidateId,
                        CandidateName = g.Key.Name??"",
                        VoteCount = g.Count()
                    })
                    .OrderByDescending(x => x.VoteCount)
                    .ToListAsync();

                response.Data = results;
                response.Success = true;
                response.Message = "Results fetched successfully";

                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Failed to fetch results";
                response.Errors = new List<string> { ex.Message };

                return response;
            }
        }
        public async Task<ServiceResponse<List<StateResult_Dto>>> GetVoteByStateViaPosition(Guid electionID, Guid positionID)
        {
            var response = new ServiceResponse<List<StateResult_Dto>>();

            try
            {
                var votes = await _context.Votes
                    .Include(v => v.Voter)
                        .ThenInclude(v => v!.State)
                    .Include(v => v.Candidate)
                    .Include(v => v.Positions).AsNoTracking()
                    .Where(v => v.ElectionId == electionID && v.PositionId == positionID)
                    .ToListAsync();

                var result = votes
                    .GroupBy(v => v.Voter?.State!.Name)
                    .Select(stateGroup => new StateResult_Dto
                    {
                        StateName = stateGroup.Key ?? "",

                        Positions = stateGroup
                            .GroupBy(v => v.Positions.Name)
                            .Select(positionGroup => new PositionVoteDto
                            {
                                PositionName = positionGroup.Key ?? "",

                                candidateVotes = positionGroup
                                    .GroupBy(v => v.Candidate.Name)
                                    .Select(candidateGroup => new CandidateVoteDto
                                    {
                                        CandidateName = candidateGroup.Key ?? "",
                                        VoteCount = candidateGroup.Count()
                                    })
                                    .OrderByDescending(x => x.VoteCount)
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList();

                response.Success = true;
                response.Message = "Results fetched successfully";
                response.Data = result;

                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Failed to fetch results";
                response.Errors = new List<string> { ex.Message };

                return response;
            }
        }
    }
}
 