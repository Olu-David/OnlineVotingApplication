using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Controllers
{
    //[Authorize(Roles = "SuperAdmin, Official")]
    public class CandidateController : Controller
    {
        private readonly iCandidateService _candidateService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _context;


        public CandidateController(iCandidateService candidateService, UserManager<ApplicationUser> userManager, AppDbContext context)
        {
            _candidateService = candidateService;
            _userManager = userManager;
            _context = context;
        }

        public IActionResult Index()
        {
            ViewBag.CandidateName = User?.Identity?.Name ?? "Candidate";

            return View();
        }
        [Authorize(Roles = "SuperAdmin")]
        [HttpGet]
        public async Task<IActionResult> CreateCandidate()
        {
            // Clean, distinct ViewBag assignments
            ViewBag.StateView = await _context.States.AsNoTracking().ToListAsync();
            ViewBag.PositionView = await _context.Position.AsNoTracking().ToListAsync();
            ViewBag.Lga = new List<LGA>(); // Empty initially until a state is chosen

            return View();
        }
        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> CreateCandidate(CandidateViewModel model, string id, string formAction)
        {
            // 1. Handle State Change Request (No-JS reload)
            if (formAction == "loadLgas")
            {
                ViewBag.StateView = await _context.States.AsNoTracking().ToListAsync();
                ViewBag.PositionView = await _context.Position.AsNoTracking().ToListAsync();
                ViewBag.Lga = await _context.Lgas
                    .AsNoTracking()
                    .Where(l => l.StateId == model.StateId)
                    .ToListAsync();

                return View(model);
            }

            // 2. Validate Form Data Before Processing
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Unable to create, check errors.";
                await RepopulateDropdowns(model.StateId);
                return View(model);
            }

            // 3. Process Execution
            var result = await _candidateService.CreateCandidateAsync(model, id);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message ?? "Failed to save candidate.";
                await RepopulateDropdowns(model.StateId);
                return View(model);
            }

            TempData["SuccessMessage"] = "Candidate Created Successfully;";
            return RedirectToAction(nameof(Index), new { id });
        }

        // Clean helper to reuse viewbag logic
        private async Task RepopulateDropdowns(Guid? stateId)
        {
            ViewBag.StateView = await _context.States.AsNoTracking().ToListAsync();
            ViewBag.PositionView = await _context.Position.AsNoTracking().ToListAsync();
            ViewBag.Lga = stateId.HasValue
                ? await _context.Lgas.AsNoTracking().Where(l => l.StateId == stateId.Value).ToListAsync()
                : new List<LGA>();
        }


        [HttpGet]
        public IActionResult SoftDelete()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoftDelete(Guid Id, string UserId)
        {
            var result = await _candidateService.SoftDeleteCandidateAsync(Id, UserId);
            if (!result.Success)
            {
                TempData["ErrorMesage"] = "Unable to delete Candidate Successfully";
                return RedirectToAction(nameof(GetAllSoftdelete), new { Id });
            }
            TempData["SuccessMessage"] = "Candidate Moved to Thrash, You can restore after 30days";
            return View();
        }
        [HttpGet]
        public IActionResult RestoreCandidate()
        {

            return View();
        }
        [HttpPost]
        public async Task<IActionResult> RestoreCandidate(Guid Id, string UserId)
        {
            var result = await _candidateService.RestoreCandidateDeleteAsync(Id, UserId);
            if (!result.Success)
            {
                return RedirectToAction(nameof(GetAllSoftdelete), new { Id });
            }
            return RedirectToAction(nameof(AllCandidate), new { Id });
        }
        [HttpGet]
        public async Task<IActionResult>GetCandidateByPosition(Guid? PositionId, int PageNumber, int PageSize)
        {
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(user==null)
            {
                return RedirectToAction("Index", "Home");
            }
            if (PageNumber < 1) PageNumber = 1;
            if(PageSize<1) PageSize = 10;

            var PositionList = await _context.States.AsNoTracking().ToListAsync();
            ViewBag.Position = new SelectList(PositionList, "Id", "Name", PositionId);

            var ViewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = Enumerable.Empty<CandidateViewModel>(),
                PageNumber = PageNumber,
                PageSize = PageSize,
                TotalItems = PositionList.Count

            };
            if(PositionId.HasValue && PositionId!=Guid.Empty)
            {
                var result = await _candidateService.GetCandidateByPositionAsync(PositionId.Value, PageNumber, PageSize);

                if(result.Success && result.Data !=null)
                {
                    ViewModel.Items= result.Data;
                    ViewModel.TotalItems = result.TotalCount;

                    var Selected = PositionList.FirstOrDefault(m => m.Id == PositionId.Value);
                    ViewBag.PositionName = Selected != null ? Selected.Name : "";
                    ViewBag.Id= Selected!.Id;
                }
               
            }
            return View(ViewModel);
        }
        [HttpGet]
        public async Task<IActionResult> AllCandidate(int pageNumber = 1, int pageSize = 10)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            // Call your exact caching service method
            List<CandidateViewModel> candidatesList = await _candidateService.GetAllCandidates(pageNumber, pageSize);

            // Build the package bundle model
            var viewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = candidatesList,
                PageNumber = pageNumber,
                PageSize = pageSize,
                // If we fetched exactly the page size number of records, assume another page could exist
                NextPage = candidatesList.Count == pageSize
            };

            return View(viewModel);
        }
        [HttpGet]
        public async Task<IActionResult> GetCandidateByState(Guid? stateId, int pageNumber = 1, int pageSize = 10)
        {
            // 1. DROPDOWN POPULATION: Fetch the available states for the UI
            var statesList = await _context.States.AsNoTracking().ToListAsync();
            ViewBag.States = new SelectList(statesList, "Id", "Name", stateId);

            // 2. PAGINATION MODEL SETUP
            var viewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = Enumerable.Empty<CandidateViewModel>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = 0
            };

            // 3. FILTERED DATA LOOKUP
            if (stateId.HasValue && stateId.Value != Guid.Empty)
            {
                // Service handles the pagination logic (Skip/Take/Validation)
                var result = await _candidateService.GetCandidateByStateAsync(stateId.Value, pageNumber, pageSize);

                if (result.Success && result.Data != null)
                {
                    viewModel.Items = result.Data;
                    viewModel.TotalItems = result.TotalCount;

                    var selectedState = statesList.FirstOrDefault(s => s.Id == stateId.Value);
                    ViewBag.SelectedStateName = selectedState?.Name;
                    ViewBag.SelectedStateId = stateId.Value;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }
            }

            return View(viewModel);
        }
        [HttpDelete]

        public async Task<IActionResult> GetAllSoftdelete(int pageNumber = 1, int pageSize = 10)
        {
            // 1. Authenticate logged-in user context identity securely from Claims
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Index", "Home");
            }

            // 2. Fetch dataset safely unpacking our service tracking envelope response
            var result = await _candidateService.GetAllSoftDeletedCandidate(userId, pageNumber, pageSize);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction("Index", "Home"); // Kick out unauthorized users
            }

            // 3.  Count ONLY rows currently occupying the soft-deleted state within 30 days
            var retentionThreshold = DateTime.UtcNow.AddDays(-30);
            int totalTrashItems = await _context.Candidate
                .CountAsync(m => m.isDeleted && m.DeletedAt >= retentionThreshold);

            // 4. Map cleanly to our unified type-safe structural wrapper package model
            var viewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = result.Data ?? Enumerable.Empty<CandidateViewModel>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalTrashItems
            };

            return View(viewModel);
        }
        [HttpGet]
        public async Task<IActionResult> GetCandidateByLga(Guid? Lgaid, int PageNumber = 1, int pageSize = 10)
        {
            // 1. Prepare data for the dropdown (LGA list)
            var LgaList = await _context.Lgas.AsNoTracking().ToListAsync();
            ViewBag.Lga = new SelectList(LgaList, "Id", "Name", Lgaid);

            // 2. Initialize the ViewModel
            var ViewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                Items = Enumerable.Empty<CandidateViewModel>(),
                PageNumber = PageNumber,
                PageSize = pageSize,
                TotalItems = 0
            };

            // 3. Only perform the search if a valid Lgaid is provided
            if (Lgaid.HasValue && Lgaid != Guid.Empty)
            {
                // 4. Call your service - the service now handles the logic and calculates TotalCount
                var result = await _candidateService.GetCandidateByLgaAsync(Lgaid.Value, PageNumber, pageSize);

                if (result.Success && result.Data != null)
                {
                    ViewModel.Items = result.Data;
                    ViewModel.TotalItems = result.TotalCount; // 

                    // Set additional view info
                    var selectedLga = LgaList.FirstOrDefault(m => m.Id == Lgaid.Value);
                    ViewBag.LgaSelectedName = selectedLga?.Name ?? "";
                }
                else
                {
                    // If result.Success is false, show the error message from the service
                    ViewData["ErrorMessage"] = result.Message;
                }
            }

            return View(ViewModel);
        }
        [HttpGet]
        public async Task<IActionResult> GetCandidatebyParty(Guid? PartyId, int PageNumber = 1, int PageSize = 10)
        {
            if (PageNumber < 1) PageNumber = 1;
            if (PageSize < 1) PageSize = 10;

            //Fetch Needed Data
            var PartyList = await _context.Party.AsNoTracking().ToListAsync();
            ViewBag.Party = new SelectList(PartyList, "id", "Name", PartyId);
            //int totalcount = await _context.Candidate.Where(m => !m.isDeleted ).CountAsync();
            var ViewModel = new PaginatedListViewModel<CandidateViewModel>
            {
                PageNumber = PageNumber,
                PageSize = PageSize,
                TotalItems = 0

            };
            if (PartyId.HasValue && PartyId.Value != Guid.Empty)
            {
                var result = await _candidateService.GetCandidateByPartyAsync(PartyId.Value, PageNumber, PageSize);
                if (result != null && result.Data != null)
                {
                    ViewModel.Items = result.Data;
                    ViewModel.TotalItems = result.TotalCount;

                    var selectedParty = PartyList.FirstOrDefault(m => m.Id == PartyId.Value);
                    ViewBag.selectedName = selectedParty?.Name;
                    ViewBag.SelectedPartyId = PartyId.Value;
                }
                else
                {
                    ViewData["ErrorMessge"] = result!.Message;
                }
            }
            return View(ViewModel);
        }
        [HttpGet]
        public async Task<IActionResult> UpdateCandidate(Guid id, CancellationToken cancellationToken)
        {
            var candidate = await _context.Candidate.FirstOrDefaultAsync(m => m.Id == id);
            if (candidate == null)
            {
                TempData["ErrorMessage"] = "Candidate not found.";
                return RedirectToAction(nameof(AllCandidate));
            }

            var formModel = new UpdateCandidateViewModel
            {
                CandidateID = candidate.Id,
                Name = candidate.Name,
                Manifesto = candidate.Manifesto,
                PartyId = candidate.PartyId,
                PositonId = candidate.PositionId,
                StateId = candidate.StateId
            };

            // 👉 POPULATE DROPDOWNS HERE FOR THE FIRST LOAD
            await PopulateDropdownsAsync(candidate.PartyId, candidate.PositionId, candidate.StateId);

            return View(formModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCandidate(UpdateCandidateViewModel model, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            var result = await _candidateService.UpdateCandidateAsync(model, userId, cancellationToken);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message;

                // 👉 REBUILD DROPDOWNS HERE SO THE PAGE DOES NOT CRASH ON RETURN!
                await PopulateDropdownsAsync(model.PartyId, model.PositonId, model.StateId);

                return View(model);
            }

            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(AllCandidate), new {model.CandidateID});
        }

        // 3. PRIVATE HELPER METHOD: Keeps your code DRY (Don't Repeat Yourself)
        private async Task PopulateDropdownsAsync(Guid? selectedParty = null, Guid? selectedPosition = null, Guid? selectedState = null)
        {
            ViewBag.Parties = new SelectList(await _context.Party.ToListAsync(), "Id", "Name", selectedParty);
            ViewBag.Positions = new SelectList(await _context.Position.ToListAsync(), "Id", "Name", selectedPosition);
            ViewBag.States = new SelectList(await _context.States.ToListAsync(), "Id", "Name", selectedState);
        }


        //// GET: api/candidates/trash
        //// This view specifically isolates items waiting for the 30-day purge
        //public async Task<IActionResult> GetTrashCanCandidates()
        //{
        //    var trashedCandidates = await _context.Candidate
        //        .IgnoreQueryFilters() // 👈 Breaks out of the default active view
        //        .Where(c => c.isDeleted)
        //        .Select(c => new
        //        {
        //            c.Id,
        //            c.Name,
        //            c.DeletedAt,
        //            DaysRemaining = 30 - (DateTime.UtcNow - (c.DeletedAt ?? DateTime.UtcNow)).Days

        //        })
        //        .ToListAsync();

        //    return Ok(trashedCandidates);
        //}



        // POST: Candidates/ConfirmSoftDelete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetCacheSoftDelete(Guid candidateId, int pageNumber = 1, int pageSize = 10)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            // Execute your exact service business logic rule method
            var result = await _candidateService.SoftDeleteCandidateAsync(candidateId, userId);

            if (result.Success)
            {
                // Evict old cache trace immediately so table updates right away
                _candidateService.ClearCandidateCache(pageNumber, pageSize);
                TempData["SuccessMessage"] = result.Message;
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Index), new { pageNumber, pageSize });
        }
    }


    }