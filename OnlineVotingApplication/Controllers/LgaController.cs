using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using Org.BouncyCastle.Bcpg.OpenPgp;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    public class LgaController : Controller
    {
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly AppDbContext _context;
        private  readonly Logger<LgaController> _logger;
        private readonly iLgaService _Lga;

        public LgaController(UserManager<ApplicationUser> userManager, AppDbContext context, Logger<LgaController> logger, iLgaService lga)
        {
            _UserManager = userManager;
            _context = context;
            _logger = logger;
            _Lga = lga;
        }

        public IActionResult Index()
        {
            return View();
        }
    
         [HttpGet]
        public IActionResult CreateLga()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>CreateLga(LgaDTO model, string Id)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var AllStateList = await _context.States.AsNoTracking().ToListAsync();
            ViewBag.State = new SelectList(AllStateList, "Id", "Name");
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(user==null|| string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User is unauthorized to perform this function";
                return RedirectToAction("Index", "Home");
            }
            var result= await _Lga.CreateLgaAsync(model, Id );
            if(!result.Success)
            {
                TempData["ErrorMessage"] = "Lga Creation was unsuccessful";
                return RedirectToAction(nameof(Index));
            }
            return RedirectToAction(nameof(AllLga), new { Id });

        }
        [HttpGet]
        public async Task<IActionResult> AllLga(int pageNumber = 1, int pageSize = 10)
        {
            // Securely check if the user identity claim exists
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User cannot perform this function.";
                return RedirectToAction("Index", "Home");
            }

            // Fetch the paginated data from the service
            var result = await _Lga.GetAllLgasAsync(pageNumber, pageSize);
            if (result == null || result.TotalItems == 0)
            {
                TempData["ErrorMessage"] = "Nothing was found. Try again or contact the administrator.";
                return NotFound();
             
            }

            // Map data to the view model
            var sendView = new PaginatedListViewModel<LgaDTO>
            {
                Items = result?.Items ?? new List<LgaDTO>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = result?.TotalItems ?? 0
            };

            return View(sendView);
        }

        [HttpGet]
        public IActionResult ConfirmDelete()
        {
            return View();  
        }
        [HttpPost]
        public async Task<IActionResult> ConfirmDelete(LgaDTO model, string ID)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User cannot Perform the Function, Authorized User only";
                return RedirectToAction("Index", "Home");
            }
            var result = await _Lga.DeleteLgaAsync(model, ID);
            if(!result.Success)
            {
                TempData["ErrorMessage"] = "Item Deleted Unsuccesful"; 
                return RedirectToAction(nameof(AllLga));
            }
            return View();
        }
    }
}
