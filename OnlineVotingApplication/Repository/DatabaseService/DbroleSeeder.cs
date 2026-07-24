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
        public static async Task SeedRolesAndUsersAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DbroleSeeder>>();

            try
            {
                await context.Database.MigrateAsync();
                logger.LogInformation("Database migration applied/verified.");

                var roles = new[] { "SuperAdmin", "Official", "Voter", "Auditor", "Candidate" };
                foreach (var role in roles)
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        await roleManager.CreateAsync(new IdentityRole(role));
                        logger.LogInformation($"Role created: {role}");
                    }
                }

                var users = new List<(ApplicationUser User, string Password, string Role)>
                {
                    (new ApplicationUser {
                        FullName = "Olusanya David Victor",
                        UserName = "superadmin",
                        Email = "superadmin@election.com",
                        EmailConfirmed = true,
                        profileImage = "",
                        StateId =null
                    }, "SecureP@ss123!", "SuperAdmin"),

                    (new ApplicationUser {
                        FullName = "Election Official",
                        UserName = "official",
                        Email = "official@election.com",
                        EmailConfirmed = true,
                        profileImage = "",
                        StateId=null
                    }, "SecureP@ss123!", "Official"),

                    (new ApplicationUser {
                        FullName = "Voter User",
                        UserName = "voter",
                        Email = "voter@election.com",
                        EmailConfirmed = true,
                        profileImage = "",
                        StateId=null
                    }, "SecureP@ss123!", "Voter"),

                    (new ApplicationUser {
                        FullName = "Auditor User",
                        UserName = "auditor",
                        Email = "auditor@election.com",
                        EmailConfirmed = true,
                        profileImage = "",
                        StateId=null
                    }, "SecureP@ss123!", "Auditor"),

                    (new ApplicationUser {
                        FullName = "Candidate User",
                        UserName = "candidate",
                        Email = "candidate@election.com",
                        EmailConfirmed = true,
                        profileImage = "",
                        StateId=null
                        
                    }, "SecureP@ss123!", "Candidate")

                };

                foreach (var item in users)
                {
                    try
                    {
                        ApplicationUser? existingUser = null;

                        try
                        {
                            existingUser = await userManager.FindByEmailAsync(item.User.Email ?? "");
                        }
                        catch (InvalidOperationException ex) when (ex.Message.Contains("more than one element"))
                        {
                            logger.LogError($"Duplicate emails found for '{item.User.Email}'. Clean up your Users table.");
                            throw;
                        }

                        if (existingUser == null)
                        {
                            try
                            {
                                existingUser = await userManager.FindByNameAsync(item.User.UserName ?? "");
                            }
                            catch (InvalidOperationException ex) when (ex.Message.Contains("more than one element"))
                            {
                                logger.LogError($"Duplicate usernames found for '{item.User.UserName}'. Clean up your Users table.");
                                throw;
                            }
                        }

                        if (existingUser == null)
                        {
                            var result = await userManager.CreateAsync(item.User, item.Password);
                            if (result.Succeeded)
                            {
                                var createdUser = await userManager.FindByEmailAsync(item.User.Email ?? "");
                                await userManager.AddToRoleAsync(createdUser!, item.Role);
                                logger.LogInformation($"Created: {item.User.Email}");
                            }
                            else
                            {
                                logger.LogWarning($"Create failed for {item.User.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                            }
                        }
                        else
                        {
                            bool changed = false;

                            if (existingUser.Email != item.User.Email)
                            {
                                existingUser.Email = item.User.Email;
                                changed = true;
                            }
                            if (existingUser.UserName != item.User.UserName)
                            {
                                existingUser.UserName = item.User.UserName;
                                changed = true;
                            }
                            if (existingUser.EmailConfirmed != item.User.EmailConfirmed)
                            {
                                existingUser.EmailConfirmed = item.User.EmailConfirmed;
                                changed = true;
                            }
                            if (existingUser.FullName != item.User.FullName)
                            {
                                existingUser.FullName = item.User.FullName;
                                changed = true;
                            }

                            if (changed)
                            {
                                bool saved = false;
                                int retries = 3;
                                while (!saved && retries > 0)
                                {
                                    try
                                    {
                                        var updateResult = await userManager.UpdateAsync(existingUser);
                                        if (updateResult.Succeeded)
                                        {
                                            saved = true;
                                            logger.LogInformation($"Updated: {existingUser.Email}");
                                        }
                                        else
                                        {
                                            logger.LogWarning($"Update failed for {existingUser.Email}: {string.Join(", ", updateResult.Errors.Select(e => e.Description))}");
                                            break;
                                        }
                                    }
                                    catch (DbUpdateConcurrencyException)
                                    {
                                        await context.Entry(existingUser).ReloadAsync();
                                        retries--;
                                        logger.LogWarning($"Concurrency conflict for {existingUser.Email}, retrying... ({retries} left)");
                                    }
                                }
                            }

                            var currentRoles = await userManager.GetRolesAsync(existingUser);
                            if (!currentRoles.Contains(item.Role))
                            {
                                await userManager.AddToRoleAsync(existingUser, item.Role);
                                logger.LogInformation($"Role '{item.Role}' added to {existingUser.Email}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"Failed to process user: {item.User.Email ?? item.User.UserName}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Seeding pipeline crashed during data execution.");
            }
        }
    }
}