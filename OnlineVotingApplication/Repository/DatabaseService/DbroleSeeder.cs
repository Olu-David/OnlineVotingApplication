using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.DatabaseService
{
    public class DbroleSeeder
    {
        // No internal 'using var scope' is created here because the scoped serviceProvider 
        // is cleanly passed in from your Program.cs initialization pipeline.
        public static async Task SeedRolesAndUsersAsync(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DbroleSeeder>>();

            try
            {
                var context = serviceProvider.GetRequiredService<AppDbContext>();

                // Applies any pending migrations cleanly to your LocalDB
                await context.Database.MigrateAsync();
                logger.LogInformation("Database migration applied/verified successfully.");
                Console.WriteLine("Database migration applied/verified successfully.");

                var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                // Seed Roles
                var roles = new[] { "SuperAdmin", "Official", "Voter", "Auditor", "Candidate" };
                foreach (var role in roles)
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                        if (roleResult.Succeeded)
                        {
                            logger.LogInformation("Role created: {Role}", role);
                            Console.WriteLine($"Role created: {role}");
                        }
                        else
                        {
                            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                            logger.LogWarning("Role creation failed for {Role}: {Errors}", role, errors);
                            Console.WriteLine($"Role creation FAILED for {role}: {errors}");
                        }
                    }
                }

                // Prepare Seed Users
                var users = new List<(ApplicationUser User, string Password, string Role)>
                {
                    (new ApplicationUser { FullName = "Olusanya David Victor", UserName = "superadmin@election.com", Email = "superadmin@election.com", EmailConfirmed = true, profileImage = "", StateId = null }, "SecureP@ss123!", "SuperAdmin"),
                    (new ApplicationUser { FullName = "Election Official", UserName = "official@election.com", Email = "official@election.com", EmailConfirmed = true, profileImage = "", StateId = null }, "SecureP@ss123!", "Official"),
                    (new ApplicationUser { FullName = "Voter User", UserName = "voter@election.com", Email = "voter@election.com", EmailConfirmed = true, profileImage = "", StateId = null }, "SecureP@ss123!", "Voter"),
                    (new ApplicationUser { FullName = "Auditor User", UserName = "auditor@election.com", Email = "auditor@election.com", EmailConfirmed = true, profileImage = "", StateId = null }, "SecureP@ss123!", "Auditor"),
                    (new ApplicationUser { FullName = "Candidate User", UserName = "candidate@election.com", Email = "candidate@election.com", EmailConfirmed = true, profileImage = "", StateId = null }, "SecureP@ss123!", "Candidate")
                };

                // Seed and Update Users
                foreach (var item in users)
                {
                    try
                    {
                        ApplicationUser? existingUser = await userManager.FindByEmailAsync(item.User.Email ?? "");

                        if (existingUser == null)
                        {
                            var result = await userManager.CreateAsync(item.User, item.Password);
                            if (result.Succeeded)
                            {
                                await userManager.AddToRoleAsync(item.User, item.Role);
                                logger.LogInformation("Successfully seeded and assigned role to user: {Email}", item.User.Email);
                                Console.WriteLine($"Successfully seeded user: {item.User.Email}");
                            }
                            else
                            {
                                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                                logger.LogWarning("Create failed for {Email}: {Errors}", item.User.Email, errors);
                                Console.WriteLine($"Create FAILED for {item.User.Email}: {errors}");
                            }
                        }
                        else
                        {
                            // Sync role if missing
                            if (!await userManager.IsInRoleAsync(existingUser, item.Role))
                            {
                                await userManager.AddToRoleAsync(existingUser, item.Role);
                                logger.LogInformation("Assigned missing role '{Role}' to existing user: {Email}", item.Role, existingUser.Email);
                                Console.WriteLine($"Assigned missing role '{item.Role}' to user: {existingUser.Email}");
                            }

                            // Sync profile details if changed
                            bool changed = false;
                            if (existingUser.FullName != item.User.FullName)
                            {
                                existingUser.FullName = item.User.FullName;
                                changed = true;
                            }

                            if (changed)
                            {
                                var updateResult = await userManager.UpdateAsync(existingUser);
                                if (updateResult.Succeeded)
                                {
                                    logger.LogInformation("Updated details for user: {Email}", existingUser.Email);
                                    Console.WriteLine($"Updated details for user: {existingUser.Email}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing seeder item for user {Email}", item.User.Email);
                        Console.WriteLine($"ERROR seeding {item.User.Email}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Global Seeder Processing failure");
                Console.WriteLine("=== GLOBAL SEEDER FAILURE ===");
                Console.WriteLine(ex.ToString());
                throw;
            }
        }
    }
}
