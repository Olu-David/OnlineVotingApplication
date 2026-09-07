using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Diagnostics;

namespace OnlineVotingApplication.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IElectionService _electionService;
        private readonly AppDbContext _Context;
        private readonly ISupportService _supportService;

        #region HomeController
        public HomeController(ILogger<HomeController> logger, IElectionService electionService, AppDbContext context, ISupportService supportService)
        {
            _logger = logger;
            _electionService = electionService;
            _Context = context;
            _supportService = supportService;
        }
        #endregion

        #region Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // 1. Fetch elections list
            var pastElectionsResponse = await _electionService.GetPastElectionsAsync();
            ViewBag.PastElections = pastElectionsResponse.Success && pastElectionsResponse.Data != null
                ? pastElectionsResponse.Data
                : new List<ElectionEvent>();

            return View();
        }
        #endregion
        #region Privacy
        // GET: /Home/Privacy
        [HttpGet]
        public IActionResult Privacy()
        {
            return View();
        }
        #endregion

        #region SubmitSupport
        // POST: /Home/SubmitSupport
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitSupport(string name, string email, string subject, string message, string IpAddress, Guid? tenantID)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(message))
            {
                TempData["SupportError"] = "Please fill out all required fields before submitting.";
                return RedirectToAction(nameof(Privacy));
            }

            // TODO: Optional - Save message to a SupportTicket database table or trigger an email notification
            await _supportService.CreateTicketAsync(name, email, subject, message, IpAddress, tenantID);

            TempData["SupportSuccess"] = "Your message has been successfully sent to our support team. We will get back to you shortly.";

            // Redirect back to the privacy page anchored directly at the form
            return Redirect($"/Home/Privacy#support-form-section");
        }
        #endregion

        #region Error
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
        #endregion

        #region SupportTickets
        [HttpGet]
        public async Task<IActionResult> SupportTickets(int pageNumber = 1, int pageSize = 10, Guid? tenantId = null)
        {
            var paginatedTickets = await _supportService.GetPaginatedTicketsAsync(pageNumber, pageSize, tenantId);

            var response = new ServiceResponse<PaginatedListViewModel<SupportTicket>>
            {
                Data = paginatedTickets,
                Success = true,
                Message = "Support tickets retrieved successfully."
            };

            return View(response);
        }
        #endregion

        #region AccessDenied
        [AllowAnonymous] // Ensures blocked users can actually open this page
        public IActionResult AccessDenied()
        {
            return View();
        }
        #endregion
    }
}