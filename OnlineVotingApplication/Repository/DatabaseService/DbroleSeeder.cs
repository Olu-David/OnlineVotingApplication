using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.DatabaseService
{
    public class DbroleSeeder
    {
        public static async Task SeedRolesAndUsersAsync(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DbroleSeeder>>();

            try
            {
                var context = serviceProvider.GetRequiredService<AppDbContext>();

                // 1. Applies pending migrations
                await context.Database.MigrateAsync();
                logger.LogInformation("Database migration applied/verified successfully.");

                var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                // -------------------------------------------------------------
                // 2. SEED DEFAULT TENANT FIRST
                // -------------------------------------------------------------
                var defaultTenant = await context.Tenants.FirstOrDefaultAsync(t => t.OrganizationName == "System Root");

                if (defaultTenant == null)
                {
                    defaultTenant = new Tenant
                    {
                        Id = Guid.NewGuid(),
                        OrganizationName = "System Root",
                        Slug = "system-root",
                        TenantCategory = TenantCategory.Custom,
                        SubscriptionPlan = "Enterprise",
                        IsActive = true,
                        IsApproved = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    await context.Tenants.AddAsync(defaultTenant);
                    await context.SaveChangesAsync(); // Flushes Tenant to DB so FK exists
                    logger.LogInformation("Default Tenant 'System Root' created with ID: {TenantId}", defaultTenant.Id);
                }
                var electionEvent = await context.ElectionEvents.FirstOrDefaultAsync(m => m.Title == "AdminVoting");

                if (electionEvent == null && !await context.ElectionEvents.AnyAsync())
                {
                    var sampleElection = new ElectionEvent
                    {
                        Id = Guid.NewGuid(),
                        Title = "General Presidential Election 2026",
                        Category = OnlineVotingApplication.Enums.TenantCategory.Political,
                        CreatedAt = DateTime.UtcNow,
                        

                    };

                    await context.ElectionEvents.AddAsync(sampleElection);
                    await context.SaveChangesAsync();
                }

                // -------------------------------------------------------------
                // 3. SEED ROLES
                // -------------------------------------------------------------
                var roles = new[] { "SuperAdmin", "Official", "Voter", "Auditor", "Candidate" };
                foreach (var role in roles)
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                        if (roleResult.Succeeded)
                        {
                            logger.LogInformation("Role created: {Role}", role);
                        }
                        else
                        {
                            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                            logger.LogWarning("Role creation failed for {Role}: {Errors}", role, errors);
                        }
                    }
                }

                // -------------------------------------------------------------
                // 4. PREPARE SEED USERS WITH VALID TENANT ID
                // -------------------------------------------------------------
                var users = new List<(ApplicationUser User, string Password, string Role)>
                {
                    (new ApplicationUser { FullName = "Olusanya David Victor", UserName = "superadmin@election.com", Email = "superadmin@election.com", EmailConfirmed = true, profileImage = "", StateId = null, TenantId = null }, "SecureP@ss123!", "SuperAdmin"),
                    (new ApplicationUser { FullName = "Election Official", UserName = "official@election.com", Email = "official@election.com", EmailConfirmed = true, profileImage = "", StateId = null, TenantId = defaultTenant.Id }, "SecureP@ss123!", "Official"),
                    (new ApplicationUser { FullName = "Voter User", UserName = "voter@election.com", Email = "voter@election.com", EmailConfirmed = true, profileImage = "", StateId = null, TenantId = defaultTenant.Id }, "SecureP@ss123!", "Voter"),
                    (new ApplicationUser { FullName = "Auditor User", UserName = "auditor@election.com", Email = "auditor@election.com", EmailConfirmed = true, profileImage = "", StateId = null, TenantId = defaultTenant.Id }, "SecureP@ss123!", "Auditor"),
                    (new ApplicationUser { FullName = "Candidate User", UserName = "candidate@election.com", Email = "candidate@election.com", EmailConfirmed = true, profileImage = "", StateId = null, TenantId = defaultTenant.Id }, "SecureP@ss123!", "Candidate")
                };

                // -------------------------------------------------------------
                // 5. SEED AND UPDATE USERS
                // -------------------------------------------------------------
                foreach (var item in users)
                {
                    try
                    {
                        ApplicationUser? existingUser = await userManager.FindByEmailAsync(item.User.Email ?? "");

                        if (existingUser == null)
                        {
                            // Ensure TenantId is explicitly set on the instance before creation
                            item.User.TenantId = defaultTenant.Id;

                            var result = await userManager.CreateAsync(item.User, item.Password);
                            if (result.Succeeded)
                            {
                                await userManager.AddToRoleAsync(item.User, item.Role);
                                logger.LogInformation("Successfully seeded and assigned role to user: {Email}", item.User.Email);
                            }
                            else
                            {
                                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                                logger.LogWarning("Create failed for {Email}: {Errors}", item.User.Email, errors);
                            }
                        }
                        else
                        {
                            // Sync missing role
                            if (!await userManager.IsInRoleAsync(existingUser, item.Role))
                            {
                                await userManager.AddToRoleAsync(existingUser, item.Role);
                                logger.LogInformation("Assigned missing role '{Role}' to existing user: {Email}", item.Role, existingUser.Email);
                            }

                            // Sync profile details and update TenantId if unassigned or invalid
                            bool changed = false;

                            if (existingUser.FullName != item.User.FullName)
                            {
                                existingUser.FullName = item.User.FullName;
                                changed = true;
                            }

                            if (existingUser.TenantId == Guid.Empty || existingUser.TenantId != defaultTenant.Id)
                            {
                                existingUser.TenantId = defaultTenant.Id;
                                changed = true;
                            }

                            if (changed)
                            {
                                var updateResult = await userManager.UpdateAsync(existingUser);
                                if (updateResult.Succeeded)
                                {
                                    logger.LogInformation("Updated details and TenantId for user: {Email}", existingUser.Email);
                                }
                                else
                                {
                                    var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                                    logger.LogWarning("Update failed for {Email}: {Errors}", existingUser.Email, errors);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing seeder item for user {Email}", item.User.Email);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Global Seeder Processing failure");
                throw;
            }
        }
    }
}