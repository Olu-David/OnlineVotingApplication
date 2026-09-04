using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Config;
using Resend;

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

            // 1. Add MVC Controllers & Views
            builder.Services.AddControllersWithViews();

            // 2. Register Resend Client using official extension methods
            builder.Services.AddOptions();
            builder.Services.AddHttpClient<IResend, ResendClient>();
            builder.Services.Configure<ResendClientOptions>(o =>
            {
                o.ApiToken = builder.Configuration["Resend:ApiKey"]
                             ?? builder.Configuration["ResendApiKey"]
                             ?? string.Empty;
            });

            // 3. Check if running under an automated xUnit test runner session
            if (builder.Environment.IsEnvironment("Testing"))
            {
                // Register In-Memory provider for testing
                builder.Services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("OnlineVotingApplicationTestDb"));

                builder.Services.AddVotingInfrastructure(builder.Configuration);
            }
            else
            {
                // Standard production infrastructure setup
                builder.Services.AddVotingInfrastructure(builder.Configuration);
            }

            builder.Services.AddCustomIdentityAndSecurity();
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // ==========================================
            // BUILD THE APPLICATION
            // ==========================================
            var app = builder.Build(); 

            // ==========================================
            // EXECUTE TASKS & MIDDLEWARE (Runtime)
            // ==========================================

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            // Prevent SQL Server migrations during integration tests
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