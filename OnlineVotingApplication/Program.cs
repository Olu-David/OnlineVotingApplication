using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Config;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using Resend;
using System.Threading.RateLimiting;

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

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Your Global Limiter...
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetSlidingWindowLimiter(clientIp, _ =>
                        new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 4,
                            QueueLimit = 5
                        });
                });

                // Your Strict Policy...
                options.AddPolicy("StrictPolicy", httpContext =>
                {
                    var identifier = httpContext.User.Identity?.IsAuthenticated == true
                        ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                    return RateLimitPartition.GetFixedWindowLimiter(identifier, _ =>
                        new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        });
                });

                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        message = "Too many failed attempts or requests. Please try again later."
                    }, token);
                };
            });

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