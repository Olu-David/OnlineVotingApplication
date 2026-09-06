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

            // 3. Infrastructure & Database setup
            if (builder.Environment.IsEnvironment("Testing"))
            {
                builder.Services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("OnlineVotingApplicationTestDb"));
            }

            builder.Services.AddVotingInfrastructure(builder.Configuration, builder.Environment);

            // 4. Identity, Security & Google Auth Middleware
            builder.Services.AddCustomIdentityAndSecurity();

            builder.Services.AddAuthentication()
                .AddGoogle(googleOptions =>
                {
                    googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
                    googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
                });

            // 5. Session Setup
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // 6. External Auth Service Registrations
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

            // 7. Advanced Rate Limiter Policy Setup
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.AddSlidingWindowLimiter("StandardPolicy", opt =>
                {
                    opt.PermitLimit = 100;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.SegmentsPerWindow = 4;
                    opt.QueueLimit = 5;
                    opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                });

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    if (context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Please try again later." }, cancellationToken);
                    }
                    else
                    {
                        context.HttpContext.Response.ContentType = "text/plain";
                        await context.HttpContext.Response.WriteAsync("Too many requests. Please slow down.", cancellationToken);
                    }
                };
            });

            // ==========================================
            // BUILD THE APPLICATION
            // ==========================================
            var app = builder.Build();

            // Respect X-Forwarded headers from Render's load balancer
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                                   Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
            });

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            if (app.Environment.EnvironmentName != "Testing")
            {
                // Ensures migrations and seeding execute smoothly using native configuration
                await app.InitializeAndSeedDatabaseAsync(app.Environment);
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();

            app.UseRateLimiter();

            // Middleware execution order
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            await app.RunAsync();
        }
    }
}