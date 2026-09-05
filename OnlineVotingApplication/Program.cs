using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Config;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using Resend;

namespace OnlineVotingApplication
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

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

            // 3. Infrastructure & Database setup
            if (builder.Environment.IsEnvironment("Testing"))
            {
                builder.Services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("OnlineVotingApplicationTestDb"));
            }

            // Register infrastructure services (channels, background workers, app services)
            // Note: VotingInfrastructureExtensions will now safely skip overriding the DbContext if environment is "Testing"
            builder.Services.AddVotingInfrastructure(builder.Configuration, builder.Environment);

            // 4. Identity, Security & Unified Rate Limiter
            builder.Services.AddCustomIdentityAndSecurity();

            // 5. Session Setup
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // 6. Google & Apple API Client Registrations
            builder.Services.AddHttpClient<IGoogleAuthService, GoogleAuthService>(client =>
            {
                client.BaseAddress = new Uri("https://www.googleapis.com/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            builder.Services.AddHttpClient<IAppleAuthService, AppleAuthService>(client =>
            {
                client.BaseAddress = new Uri("https://appleid.apple.com/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // Register the "StandardPolicy" required by your endpoints
            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter("StandardPolicy", opt =>
                {
                    opt.PermitLimit = 100;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 2;
                });
            });

            // ==========================================
            // BUILD THE APPLICATION (Called ONLY ONCE)
            // ==========================================
            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            if (app.Environment.EnvironmentName != "Testing")
            {
                await app.InitializeAndSeedDatabaseAsync();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();

            // Must be placed after UseRouting and before UseAuthentication/Authorization
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