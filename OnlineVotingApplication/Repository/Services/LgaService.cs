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
        public LgaService(AppDbContext context, UserManager<ApplicationUser> userManager, IMemoryCache cache)
        {
            _context = context;
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

            catch (Exception ex)
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

        public async Task<PaginatedListViewModel<LgaDTO>> GetAllLgasAsync(int PageNumber = 1, int PageSize = 10)
        {
            PageNumber = Math.Max(1, PageNumber);
            PageSize = Math.Max(1, PageSize);


            var cacheKey = "all_lgas";
            var queryDb = await _context.Lgas.AsNoTracking().ToListAsync();
            int ItemsCount = queryDb.Count;

            if (!_cache.TryGetValue(cacheKey, out List<LgaDTO>? lgas))
            {
                lgas = queryDb
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
        public async Task<PaginatedListViewModel<LgaDTO>> GetLgasByStateIdAsync(Guid stateId, int PageNumber = 1, int PageSize = 10)
        {
            var cacheKey = $"lgas_state_{stateId}_{PageNumber}_{PageSize}";

            var queryDb = _context.Lgas
                    .Where(x => x.StateId == stateId);
            int ItemCount = queryDb.Count();
            if (!_cache.TryGetValue(cacheKey, out List<LgaDTO>? lgas))
            {
                lgas = await queryDb
                    .OrderBy(x => x.Name)
                    .Select(x => new LgaDTO
                    {
                        Id = x.Id,
                        Name = x.Name,
                        StateId = x.StateId
                    })
                    .ToListAsync();

                _cache.Set(cacheKey, lgas, TimeSpan.FromMinutes(5));
            }

            return new PaginatedListViewModel<LgaDTO>
            {
                Items = lgas != null ? lgas : Enumerable.Empty<LgaDTO>(),
                TotalItems = ItemCount,
                PageSize = PageSize,
                PageNumber = PageNumber
            };
        }



        public async Task<LGA?> GetLgaByIdAsync(Guid id)
        {
            return await _context.Lgas.FirstOrDefaultAsync(m => m.Id == id);
        }

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
            var LGA = await _context.Lgas.FirstOrDefaultAsync(m => m.Id == dto.Id && m.Name == dto.Name);
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
           
    }
}

