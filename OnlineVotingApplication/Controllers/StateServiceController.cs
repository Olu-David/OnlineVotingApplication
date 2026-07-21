using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    public class StateServiceController : Controller
    {
        private readonly iStateService _StateService;
        private readonly AppDbContext _context;
        private readonly ILogger<StateServiceController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public StateServiceController(iStateService stateService, AppDbContext context, ILogger<StateServiceController> logger, UserManager<ApplicationUser> userManager)
        {
            _StateService = stateService;
            _context = context;
            _logger = logger;
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult CreateStateAsync ()=> View();
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStateAsync(StateDTO model)
        {
            var user= User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(user==null)
            {
                return RedirectToAction("Index", "Home");
            }
            if(!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Unable to create state";
                foreach(var item in ModelState)
                {
                    var fieldname= item.Key;
                    var errors = item.Value.Errors;

                    foreach(var error in errors)
                    {
                        // Use error.ErrorMessage to read the validation failure text
                        System.Diagnostics.Debug.WriteLine($"Field: {fieldname} - Error: {error.ErrorMessage}");
                    }

                }
                return View(model);
            }
            var result= await _StateService.CreateStateAsync(model);
            if(result==false)
            {
                TempData["ErrorMessage"] = "Unable to create state detail";
                return RedirectToAction(nameof(Index), "Home");
            }
            return View(result);
        }

    }
}
