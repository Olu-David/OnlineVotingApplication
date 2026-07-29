using MailKit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    public class PositionController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        //private readonly Logger<PositionController> _logger;
        private readonly AppDbContext _context;
        private readonly iPositionService _Pos;

        public PositionController(UserManager<ApplicationUser> userManager, AppDbContext context, iPositionService pos)
        {
            _userManager = userManager;
            //_logger = logger;
            _context = context;
            _Pos = pos;
        }

        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult CreatePosition()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePosition(PositionDTO model, string userId)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (user == null)
            {
                return RedirectToAction("Index", "Home");
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var result = await _Pos.CreatePositionAsync(model, userId);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Unable to Create Postion";

            }
            return View(result);

        }
        [HttpGet]
        public async Task<IActionResult> EditPosition(Guid Id)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User Unauthorized to perform this task. Only Admins or Officials can.";
                return RedirectToAction("Index", "Home");
            }
            var EditPosition = await _context.Position.FirstOrDefaultAsync(m => m.Id == Id);
            if (EditPosition == null)
            {
                TempData["ErrorMessage"] = "The requested Position could not be found.";
                return NotFound();
            }
            var SendPos = new EditPositionModel
            {
                Name = EditPosition?.Name ?? ""
            };
            return View(SendPos);


        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPosition(EditPositionModel model, string Id)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User Unauthorized to perform this task. Only Admins or Officials can.";
                return RedirectToAction("Index", "Home");
            }
            var result = await _Pos.UpdatePosition(model, Id);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = "Position Edited unsucessfully";
            }
            return RedirectToActionPermanent(nameof(AllPosition));

        }
        public async Task<IActionResult> AllPosition(int pageNumber = 1, int pageSize = 10)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                TempData["ErrorMessage"] = "User Unauthorized to perform this task. Only Admins or Officials can.";
                return RedirectToAction("Index", "Home");
            }

            // The service returns the exact, complete model the View needs
            var viewModel = await _Pos.GetAllPositionsAsync(userId, pageNumber, pageSize);

            // Change .Count to .Count()
            if (viewModel.Items == null || !viewModel.Items.Any())
            {
                TempData["ErrorMessage"] = "Position Search returns empty";
                return RedirectToAction("Index", "Home");
            }

            return View(viewModel); // Directly pass it to your Index / AllPosition view
        }

    }
}
