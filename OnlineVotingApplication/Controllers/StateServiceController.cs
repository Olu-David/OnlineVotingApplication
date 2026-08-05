using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using StackExchange.Redis;
using System.Security.Claims;
using System.Threading.Tasks;

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
        [Authorize(Roles = "SuperAdmin")]
        [HttpGet]
        public IActionResult CreateState()
        {
            return View();
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateState(StateDTO model)
        {
            // 1. Get the raw String User ID from the logged-in ClaimsPrincipal
            string? userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User not found or session expired.";
                return RedirectToAction("Index", "Home");
            }

            // 2. Validate incoming ModelState bindings
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Unable to create state due to validation errors.";

                foreach (var item in ModelState)
                {
                    var fieldname = item.Key;
                    var errors = item.Value.Errors;

                    foreach (var error in errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"Field: {fieldname} - Error: {error.ErrorMessage}");
                    }
                }
                return View(model);
            }

            // 3. Generate the ID here if it doesn't exist so your redirect route works!
            if (model.Id == null)
            {
                model.Id = Guid.NewGuid();
            }

            // 4. Call your State Service passing the safe parameters
            var result = await _StateService.CreateStateAsync(model, userId);
            if (result == false)
            {
                TempData["ErrorMessage"] = "Unable to create state details database record.";
                return View(model); // Return the view with the model so the user doesn't lose their typed input!
            }

            // 5. Safely redirect to your tracking action now that model.Id is guaranteed to exist
            TempData["SuccessMessage"] = "State created successfully!";
            return RedirectToAction(nameof(AllState), new { id = model.Id });
        }

        [HttpGet]
        public async Task<IActionResult> EditState(Guid id)
        {
            // 1. Audit check for logged-in user
            var user = _userManager.GetUserId(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User Unauthorized to perform this task. Only Admins or Officials can.";
                return RedirectToAction("Index", "Home"); 
            }

            // 2. Fetch from your state service layer
            var stateData = await _StateService.GetStateByIdAsync(id);
            if (stateData == null)
            {
                TempData["ErrorMessage"] = "The requested state could not be found.";
                return NotFound();
            }

            // 3. Map your service domain model directly to your DTO
            var model = new UpdateStateDto
            {
                Id = stateData.Id,
                Name = stateData.Name
            };

            // 4. Fixed: Pass the model to the View so the HTML inputs can pre-fill!
            return View(model);
        }

      

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditState(string Id, UpdateStateDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var result = await _StateService.UpdateStateAsync(model, Id);
            if(!result)
            {
                TempData["ErrorMessage"] = "Unable to Edit State";
                return View(model);
            }
            TempData["SucessMessage"] = "State Update Successful";
            return RedirectToAction(nameof(AllState), new { model.Id });
            


        }
        [HttpGet]
        public async Task<IActionResult> AllState()
        {
           var user= User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(user==null)
            {
                TempData["ErrorMessage"] = "User not Found";
                return RedirectToAction("Index", "Home");
            }
            int Allstate = await _context.States.CountAsync();
            ViewBag.StateCount = Allstate;
            var result = await _StateService.GetAllStatesAsync();
            if(result.Count==0)
            {
                TempData["ErrorMessage"] = "State search returned nothing";
                return View(result);
            }
            return View(result);
        }
        [HttpGet]
        public IActionResult ConfirmSoftDelete()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> ConfirmStateDelete(Guid Id)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(user==null)
            {
                TempData["ErrorMessage"] = "User not found/Unathorized to perform this function";
                return RedirectToAction("Index", "Home");
            }
            var result = await _StateService.DeleteStateAsync(Id);
            if(!result)
            {
                TempData["ErrorMessage"] = "State deleted unsuccessful";
                return View();
            }
            return View(nameof(AllState));

        }
       

    }
}
