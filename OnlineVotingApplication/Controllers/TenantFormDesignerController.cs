using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Repository.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TenantFormDesignerController : Controller
    {
        private readonly AppDbContext _context;
        private readonly HybridFormBuilderService _formBuilderService;

        public TenantFormDesignerController(AppDbContext context, HybridFormBuilderService formBuilderService)
        {
            _context = context;
            _formBuilderService = formBuilderService;
        }

        [HttpGet]
        public async Task<IActionResult> ManageForm(Guid electionId)
        {
            var activeFields = await _context.ElectionCustomFields
                .Where(f => f.ElectionEventId == electionId)
                .ToListAsync();

            ViewBag.ElectionId = electionId;
            return View(activeFields);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyBlueprint(Guid electionId, string templateCategory)
        {
            if (electionId == Guid.Empty || string.IsNullOrWhiteSpace(templateCategory))
            {
                return RedirectToAction("ManageForm", new { electionId });
            }

            await _formBuilderService.ApplyCategoryBlueprintToElectionAsync(electionId);

            TempData["SuccessMessage"] = "Blueprint package cloned into your form layout successfully.";
            return RedirectToAction("ManageForm", new { electionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCustomField(Guid electionId, string fieldName, ElectionFieldType fieldType, string? csvChoices, bool isRequired)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                TempData["ErrorMessage"] = "Custom question text cannot be empty.";
                return RedirectToAction("ManageForm", new { electionId });
            }

            await _formBuilderService.AddTenantCustomFieldAsync(electionId, fieldName, fieldType, csvChoices, isRequired);

            TempData["SuccessMessage"] = "Custom field added to your form questionnaire.";
            return RedirectToAction("ManageForm", new { electionId });
        }
    }
}
