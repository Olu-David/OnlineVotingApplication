using Microsoft.AspNetCore.Identity;
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
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMemoryCache _cache;
public LgaService(AppDbContext context, IHttpContextAccessor contextAccessor, UserManager<ApplicationUser> userManager, IMemoryCache cache)
        {
            _context = context;
            _contextAccessor = contextAccessor;
            _userManager = userManager;
            _cache = cache;
        }

        public async Task<ServiceResponse<string>> CreateLgaAsync(LgaDTO lga, string Id)
        {

            var response = new ServiceResponse<string>();
            var transaction = await _context.Database.BeginTransactionAsync();
            try
            {

                var user = await _userManager.FindByIdAsync(Id); 
                if(user== null)
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
                var createLGA = await _context.Lgas.AnyAsync(m => m.Id == lga.Id && m.Name == lga.Name);
                if (createLGA)
                {
                    response.Success = false;
                    response.Message = "LGA exists Already, try Another one";
                    return response;
                }

                var newLGA = new LGA
                {
                    Name = lga.Name,
                    StateId = lga.StateId
                };
                _context.Lgas.Add(newLGA);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                response.Success = true;
                response.Message = "LGA Saved Succesfully to the database";
                return response;
            }

            catch(Exception ex)
            {
                response.Success = false;
                response.Message = "An Unexpected Error Occured";
                response.Errors = new List<string>
                {
                    ex.Message
                };
                return response;
            }
            
           
        }

        public async Task<List<LgaDTO>> GetAllLgasAsync()
        {
            var cacheKey = "all_lgas";

            if (!_cache.TryGetValue(cacheKey, out List<LgaDTO>? lgas))
            {
                lgas = await _context.Lgas
                    .Select(l => new LgaDTO
                    {
                        Id = l.Id,
                        Name = l.Name,
                        StateId = l.StateId
                    })
                    .ToListAsync();

                _cache.Set(cacheKey, lgas, TimeSpan.FromMinutes(30));
            }

            return lgas!;
        }
        public async Task<List<LgaDTO>> GetLgasByStateIdAsync(Guid stateId)
        {
            var cacheKey = $"lgas_state_{stateId}";

            if (!_cache.TryGetValue(cacheKey, out List<LgaDTO>? lgas))
            {
                lgas = await _context.Lgas
                    .Where(x => x.StateId == stateId)
                    .Select(x => new LgaDTO
                    {
                        Id = x.Id,
                        Name = x.Name,
                        StateId = x.StateId
                    })
                    .ToListAsync();

                _cache.Set(cacheKey, lgas, TimeSpan.FromMinutes(5));
            }

            return lgas!;
        }


       
        public async Task<LGA?> GetLgaByIdAsync(Guid id)
        {
            return await _context.Lgas.FirstOrDefaultAsync(m=>m.Id==id);
        }

        public async Task<ServiceResponse<string>> DeleteLgaAsync(LgaDTO dto)
        {

            var response = new ServiceResponse<string>();
            var user = _contextAccessor?.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                response.Success = false;
                response.Message = "User Unathorized for this function";
                return response;
            }
            bool isAdmin = user.IsInRole("SuperAdmin"), isOfficial = user.IsInRole("Official");
            if (!isAdmin || isOfficial)
            {
                response.Success = false;
                response.Message = "Only Authorized user have Access to this feature";
                return response;
            }

            var createLGA = await _context.Lgas.FirstOrDefaultAsync(m => m.Id == dto.Id && m.Name == dto.Name);
            if (createLGA ==null)
            {
                response.Success = false;
                response.Message = "LGA does not exist";
                return response;
            }
            _context.Lgas.Remove(createLGA);
            await _context.SaveChangesAsync();

           
            response.Success = true;
            response.Message = "LGA Saved Succesfully to the database";
            return response;
          

            
        }

       
    }
}
