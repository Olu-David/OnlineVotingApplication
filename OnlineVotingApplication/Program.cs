using Microsoft.EntityFrameworkCore; // 👈 Add this if needed to reference UseInMemoryDatabase
using OnlineVotingApplication.Areas.Identity.Data; // 👈 Change to match your AppDbContext namespace
using OnlineVotingApplication.Config;

namespace OnlineVotingApplication
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ==========================================
            // REGISTER SERVICES (Toolbox Configuration)
            // ==========================================

            // 1. Check if we are running under an automated xUnit test runner session
            if (builder.Environment.IsEnvironment("Testing"))
            {
                // Register ONLY the clean In-Memory provider directly
                builder.Services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("OnlineVotingApplicationTestDb"));

                // Pass a flag or customize this call if AddVotingInfrastructure internal logic crashes without SQL Server
                builder.Services.AddVotingInfrastructure(builder.Configuration);
            }
            else
            {
                // Run your standard production database setup routine
                builder.Services.AddVotingInfrastructure(builder.Configuration);
            }
         
            builder.Services.AddCustomIdentityAndSecurity();
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });
          
            var app = builder.Build();

            // ==========================================
            // EXECUTE TASKS & MIDDLEWARE (Runtime)
            // ==========================================

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            // This is perfect! It prevents SQL Server migrations from crashing your tests.
            if (app.Environment.EnvironmentName != "Testing")
            {
                await app.InitializeAndSeedDatabaseAsync();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            await app.RunAsync();
        }
    }
}
