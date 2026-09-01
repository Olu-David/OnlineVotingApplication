using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles ="SuperAdmin")]
  
    public class PartyController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<PartyController> _logger;
        private readonly IPartyService _party;
        private readonly AppDbContext _context;

        public PartyController(UserManager<ApplicationUser> userManager, ILogger<PartyController> logger, IPartyService party, AppDbContext context)
        {
            _userManager = userManager;
            _logger = logger;
            _party = party;
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult CreateParty()
        {
            return View();
        }
        [HttpGet]
        public  async Task<IActionResult> RestoreSoftDeleted(Guid PartyId)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }
            var result = await _party.RestoreDeletedParty(user, PartyId);
            if(!result.Success)
            {
                TempData["ErrorMessage"] = "Can not restore data try again";
                return RedirectToAction(nameof(Index));
            }
            TempData["SuccessMessage"] = "Data restored succesfully"
;            return RedirectToAction(nameof(AllParty), new { PartyId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateParty(PartyViewModel model)
        {
            var user = _userManager.GetUserId(User);
            if(user==null)
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }
            if (!ModelState.IsValid)
            {
                    // DEBUG TRACKER: Gathers every validation error and prints it explicitly onto your screen banner
                    var validationErrors = string.Join(" | ", ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));

                    TempData["ErrorMessage"] = $"Unable to create. Validation Errors: {validationErrors}";
                    return View(model);
                

            }
            var result = await _party.CreatePartyAsync(model, user);
            if(!result.Success)
            {
                TempData["ErrorMessage"] = "Unable to create Party";
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = "Party has been created successful";
            return RedirectToAction(nameof(AllParty), new { model.Id });
        }
        [HttpGet]
        public async Task<IActionResult> AllParty(int PageNumber=1, int Pageize=10)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.AllPartyAsync(PageNumber, Pageize);

            if(!result.Items.Any()||result.Items==null)
            {
                return View(result);
            }

            var newView = new PaginatedListViewModel<PartyViewModel>
            {

                Items = result.Items,
                PageNumber = PageNumber,
                PageSize = Pageize,
                TotalItems
                = result.TotalItems
            };
            return View(newView);   
        }
        [HttpGet]
        public async Task<IActionResult> AllSoftDeleted(int PageNumber = 1, int Pageize = 10)
        {
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User doesn't exist";
                return RedirectToAction("Index", "Home");
            }

            var result = await _party.AllSoftDeleteAsync(PageNumber, Pageize);

            if (!result.Items.Any() || result.Items == null)
            {
                return View(result);
            }

            var newView = new PaginatedListViewModel<PartyViewModel>
            {

                Items = result.Items,
                PageNumber = PageNumber,
                PageSize = Pageize,
                TotalItems
                = result.TotalItems
            };
            return View(newView);
        }
    }
}
