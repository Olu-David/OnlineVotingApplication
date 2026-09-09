using Microsoft.AspNetCore.Authentication;
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
            Console.WriteLine("###### RUNNING UPDATED CODE ######");

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

            // 3. 🔥 ADD IDENTITY & SECURITY FIRST (UserManager, RoleManager, Cookies, RateLimiter)
            builder.Services.AddCustomIdentityAndSecurity();

            // 4. Infrastructure & Database setup (now Identity is available)
            if (builder.Environment.IsEnvironment("Testing"))
            {
                builder.Services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("OnlineVotingApplicationTestDb"));
            }

            builder.Services.AddVotingInfrastructure(builder.Configuration, builder.Environment);

            // 5. Google Authentication (uses Identity cookies)
            builder.Services.AddAuthentication()
                .AddGoogle(googleOptions =>
                {
                    googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
                    googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
                    googleOptions.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    googleOptions.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    googleOptions.CorrelationCookie.HttpOnly = true;
                    googleOptions.Events.OnRemoteFailure = context =>
                    {
                        context.Response.Redirect("/Home/Index?error=OAuthFailed");
                        context.HandleResponse();
                        return Task.CompletedTask;
                    };
                });

            // 6. Session Setup
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // 7. External Auth Service Registrations – already done inside AddVotingInfrastructure
            // No need to add them again here.

            // 8. (Optional) Additional RateLimiter policies – but we already have a global one from AddCustomIdentityAndSecurity.
            // We'll keep the container clean by not duplicating it.

            // ==========================================
            // BUILD THE APPLICATION
            // ==========================================
            var app = builder.Build();

            // Respect X-Forwarded headers from Render's load balancer
            var forwardedOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                                    Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
            };
            forwardedOptions.KnownNetworks.Clear();
            forwardedOptions.KnownProxies.Clear();

            app.UseForwardedHeaders(forwardedOptions);

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            // Force HTTPS scheme for absolute URLs
            app.Use((context, next) =>
            {
                context.Request.Scheme = "https";
                return next();
            });

            if (app.Environment.EnvironmentName != "Testing")
            {
                // Ensures migrations and seeding execute smoothly using native configuration
                await app.InitializeAndSeedDatabaseAsync(app.Environment);
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();

            app.UseRateLimiter(); // The global rate limiter from AddCustomIdentityAndSecurity

            // Middleware execution order
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // Use the PORT variable dynamically without hardcoding "http://"
            var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
            app.Urls.Add($"http://0.0.0.0:{port}");

            await app.RunAsync();
        }
    }
}