using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using OnlineVotingApplication.Areas.Identity.Data;

namespace OnlineVotingApplication.Repository.DatabaseService
{
    public  class DbroleSeeder
    {
        public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DbroleSeeder>>();

            try
            {
                logger.LogInformation("Seeding Roles...");

                var roles = new[] { "SuperAdmin", "Official", "Voter", "Auditor", "Candidate" };

                foreach (var role in roles)
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        await roleManager.CreateAsync(new IdentityRole(role));
                        logger.LogInformation($"Role created: {role}");
                    }
                }

                logger.LogInformation("Seeding Users...");
                //ApplicationUser

                var users = new List<(ApplicationUser User, string Password, string Role)>
            {
                (new ApplicationUser {
                    FullName = "Olusanya David Victor",
                    UserName = "superadmin",
                    Email = "superadmin@election.com",
                    EmailConfirmed = true
                }, "SuperAdmin@123", "SuperAdmin"),

                (new ApplicationUser {
                    FullName = "Election Official",
                    UserName = "official",
                    Email = "official@election.com",
                    EmailConfirmed = true
                }, "Official@123", "Official"),

                (new ApplicationUser {
                    FullName = "Voter User",
                    UserName = "voter",
                    Email = "voter@election.com",
                    EmailConfirmed = true
                }, "Voter@123", "Voter"),

                (new ApplicationUser {
                    FullName = "Auditor User",
                    UserName = "auditor",
                    Email = "auditor@election.com",
                    EmailConfirmed = true
                }, "Auditor@123", "Auditor"),

                (new ApplicationUser {
                    FullName = "Candidate User",
                    UserName = "candidate",
                    Email = "candidate@election.com",
                    EmailConfirmed = true
                }, "Candidate@123", "Candidate")
            };

                foreach (var item in users)
                {
                    var existingUser = await userManager.FindByEmailAsync(item.User.Email??"");

                    if (existingUser == null)
                    {
                        var result = await userManager.CreateAsync(item.User, item.Password);

                        if (result.Succeeded)
                        {
                            var createdUser = await userManager.FindByEmailAsync(item.User.Email??"");

                            await userManager.AddToRoleAsync(createdUser!, item.Role);

                            logger.LogInformation($"User created: {item.User.Email}");
                        }
                        else
                        {
                            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                            logger.LogWarning($"User failed: {item.User.Email} | {errors}");
                        }
                    }
                    else
                    {
                        logger.LogInformation($"User exists: {item.User.Email}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Seeding failed");
            }
        }
    }
}