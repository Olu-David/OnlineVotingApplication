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
                // 2. SEED DEFAULT TENANT & ELECTION EVENT FIRST
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
                    await context.SaveChangesAsync();
                    logger.LogInformation("Default Tenant 'System Root' created with ID: {TenantId}", defaultTenant.Id);
                }

                // Check for existence specifically by title
                string defaultElectionTitle = "General Presidential Election 2026";
                var electionEvent = await context.ElectionEvents.IgnoreQueryFilters() .FirstOrDefaultAsync(m => m.Title == defaultElectionTitle);
                if (electionEvent == null)
                {
                    var sampleElection = new ElectionEvent
                    {
                        Id = Guid.NewGuid(),
                        Title = defaultElectionTitle,
                        Category = TenantCategory.Political,
                        TenantId = defaultTenant.Id, // <-- Crucial: Tie election to default tenant
                        CreatedAt = DateTime.UtcNow
                    };

                    await context.ElectionEvents.AddAsync(sampleElection);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Default ElectionEvent '{Title}' created.", defaultElectionTitle);
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
                // 4. PREPARE SEED USERS 
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
                foreach (var (user, password, role) in users)
                {
                    try
                    {
                        var existingUser = await userManager.FindByEmailAsync(user.Email ?? "");
                        bool isSuperAdmin = role == "SuperAdmin";

                        if (existingUser == null)
                        {
                            // SuperAdmin is guaranteed null; standard roles get the defaultTenant.Id
                            user.TenantId = isSuperAdmin ? null : defaultTenant.Id;

                            var result = await userManager.CreateAsync(user, password);
                            if (result.Succeeded)
                            {
                                await userManager.AddToRoleAsync(user, role);
                                logger.LogInformation("Successfully seeded and assigned role to user: {Email}", user.Email);
                            }
                            else
                            {
                                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                                logger.LogWarning("Create failed for {Email}: {Errors}", user.Email, errors);
                            }
                        }
                        else
                        {
                            // Sync missing role
                            if (!await userManager.IsInRoleAsync(existingUser, role))
                            {
                                await userManager.AddToRoleAsync(existingUser, role);
                                logger.LogInformation("Assigned missing role '{Role}' to existing user: {Email}", role, existingUser.Email);
                            }

                            // Sync profile details and handle TenantId carefully
                            bool changed = false;

                            if (existingUser.FullName != user.FullName)
                            {
                                existingUser.FullName = user.FullName;
                                changed = true;
                            }

                            if (isSuperAdmin)
                            {
                                if (existingUser.TenantId != null)
                                {
                                    existingUser.TenantId = null;
                                    changed = true;
                                }
                            }
                            else
                            {
                                if (existingUser.TenantId == Guid.Empty || existingUser.TenantId != defaultTenant.Id)
                                {
                                    existingUser.TenantId = defaultTenant.Id;
                                    changed = true;
                                }
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
                        logger.LogError(ex, "Error processing seeder item for user {Email}", user.Email);
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