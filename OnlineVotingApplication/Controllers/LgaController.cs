using Microsoft.AspNetCore.Authorization;
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
    [Authorize(Roles = "SuperAdmin")]
    public class LgaController : Controller
    {

        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly AppDbContext _context;
        private readonly iLgaService _Lga;

        public LgaController(UserManager<ApplicationUser> userManager, AppDbContext context, iLgaService lga)
        {
            _UserManager = userManager;
            _context = context;
            _Lga = lga;
        }

        public IActionResult Index()
        {
            return View();
        }


        [HttpGet]
        public async Task<IActionResult> CreateLga()
        {
            // 1. Fetch data from your database context
            var allStateList = await _context.States
                                           .AsNoTracking()
                                           .OrderBy(s => s.Name)
                                           .ToListAsync();

            // 2. Wrap it into a SelectList matching the exact casing "State" in ViewBag
            ViewBag.State = new SelectList(allStateList, "Id", "Name");

            // 3. Send a clean, empty model to the view
            return View(new LgaDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLga(LgaDTO model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var allStateList = await _context.States.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
            ViewBag.State = new SelectList(allStateList, "Id", "Name", model.StateId);
            var AllStateList = await _context.States.AsNoTracking().ToListAsync();
            ViewBag.State = new SelectList(AllStateList, "Id", "Name", model.Id);
            var user = _UserManager.GetUserId(User);
            if (user == null || string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User is unauthorized to perform this function";
                return RedirectToAction("Index", "Home");
            }
            var result = await _Lga.CreateLgaAsync(model, user);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Lga Creation was unsuccessful";
                return RedirectToAction(nameof(Index));
            }
            return RedirectToAction(nameof(AllLga), new { model.Id });

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
        public async Task<IActionResult> ConfirmDelete(Guid id)
        {
           
            var lgaRecord = await _context.Lgas
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (lgaRecord == null)
            {
                TempData["ErrorMessage"] = "The requested LGA could not be found.";
                return RedirectToAction(nameof(AllLga));
            }

            // Map the database entity values over to your tracking DTO
            var model = new LgaDTO
            {
                Id = lgaRecord.Id,
                Name = lgaRecord.Name,
                StateId = lgaRecord.StateId
            };

            // Explicitly target the custom path to bypass folder directory bugs
            return View("~/Views/Lga/ConfirmDelete.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDelete(LgaDTO model)
        {
            var user = _UserManager.GetUserId(User);
            if (string.IsNullOrEmpty(user))
            {
                TempData["ErrorMessage"] = "User cannot Perform the Function, Authorized User only";
                return RedirectToAction("Index", "Home");
            }

            var result = await _Lga.DeleteLgaAsync(model, user);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Item Deleted Unsuccessful";
                return RedirectToAction(nameof(AllLga));
            }

            return RedirectToAction(nameof(DeletedSuccessfully));
        }

        // 3. SUCCESS SCREEN: Unchanged (Kept your working path redirect)
        [HttpGet]
        public IActionResult DeletedSuccessfully()
        {
            return View("~/Views/Lga/DeletedSuccessfully.cshtml");
        }
    }
}