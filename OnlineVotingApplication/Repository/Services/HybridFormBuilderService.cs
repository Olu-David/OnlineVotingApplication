using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Repository.Services
{
    public class HybridFormBuilderService
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;

        public HybridFormBuilderService(AppDbContext context, ITenantProvider tenantProvider)
        {
            _context = context;
            _tenantProvider = tenantProvider;
        }

        public async Task<bool> CreateGlobalCategoryFieldAsync(
            string fieldName,
            ElectionFieldType fieldType,
            TenantCategory category,
            string? csvChoices,
            bool isRequired)
        {
            var globalBlueprintField = new ElectionCustomField
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                ElectionEventId = Guid.Empty,
                FieldName = fieldName,
                FieldType = fieldType,
                FormCategory = category,
                CustomChoicesCsv = csvChoices,
                IsRequired = isRequired
            };

            await _context.ElectionCustomFields.AddAsync(globalBlueprintField);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ApplyCategoryBlueprintToElectionAsync(Guid electionId)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var currentTenant = await _context.Tenants.FirstOrDefaultAsync(m => m.Id == activeTenantId);
            if (currentTenant == null) return false;

            var election = await _context.ElectionEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == electionId);

            if (election == null) return false;

            if (election.Category != currentTenant.TenantCategory)
            {
                return false;
            }

            var blueprints = await _context.ElectionCustomFields
                .IgnoreQueryFilters()
                .Where(f => f.TenantId == null && f.FormCategory == election.Category)
                .ToListAsync();

            foreach (var bp in blueprints)
            {
                await _context.ElectionCustomFields.AddAsync(new ElectionCustomField
                {
                    Id = Guid.NewGuid(),
                    TenantId = activeTenantId,
                    ElectionEventId = electionId,
                    FieldName = bp.FieldName,
                    FieldType = bp.FieldType,
                    FormCategory = bp.FormCategory,
                    CustomChoicesCsv = bp.CustomChoicesCsv,
                    IsRequired = bp.IsRequired
                });
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> AddTenantCustomFieldAsync(
            Guid electionId,
            string fieldName,
            ElectionFieldType fieldType,
            string? csvChoices,
            bool isRequired)
        {
            Guid activeTenantId = _tenantProvider.GetCurrentTenantId();

            var currentTenant = await _context.Tenants.FirstOrDefaultAsync(m => m.Id == activeTenantId);
            var election = await _context.ElectionEvents.FirstOrDefaultAsync(e => e.Id == electionId);

            if (currentTenant == null || election == null) return false;

            if (election.Category != currentTenant.TenantCategory) return false;

            var tenantCustomField = new ElectionCustomField
            {
                Id = Guid.NewGuid(),
                TenantId = activeTenantId,
                ElectionEventId = electionId,
                FieldName = fieldName,
                FieldType = fieldType,
                FormCategory = TenantCategory.Custom,
                CustomChoicesCsv = csvChoices,
                IsRequired = isRequired
            };

            await _context.ElectionCustomFields.AddAsync(tenantCustomField);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
