using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Repository.DatabaseService;
using System.Threading.RateLimiting;

namespace OnlineVotingApplication.Config;

public static class IdentityAndSecurityExtensions
{
    public static IServiceCollection AddCustomIdentityAndSecurity(this IServiceCollection services)
    {
        // 1. DDoS and Brute Force Protection Engine (Rate Limiter)
        //services.AddRateLimiter(options =>
        //{
        //    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        //    // Global sliding window limiter (Protects all general routes by IP)
        //    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        //    {
        //        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        //        return RateLimitPartition.GetSlidingWindowLimiter(clientIp, _ =>
        //            new SlidingWindowRateLimiterOptions
        //            {
        //                PermitLimit = 20,
        //                Window = TimeSpan.FromMinutes(1),
        //                SegmentsPerWindow = 4,
        //                QueueLimit = 5
        //            });
        //    });

        //    // Strict policy for critical/sensitive actions (Voting, Registration, Auth endpoints)
        //    options.AddPolicy("StrictPolicy", httpContext =>
        //    {
        //        var identifier = httpContext.User.Identity?.IsAuthenticated == true
        //            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        //              ?? httpContext.Connection.RemoteIpAddress?.ToString()
        //            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        //        return RateLimitPartition.GetFixedWindowLimiter(identifier, _ =>
        //            new FixedWindowRateLimiterOptions
        //            {
        //                PermitLimit = 10,
        //                Window = TimeSpan.FromMinutes(1),
        //                QueueLimit = 0
        //            });
        //    });

        //    options.OnRejected = async (context, token) =>
        //    {
        //        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        //        context.HttpContext.Response.ContentType = "application/json";
        //        await context.HttpContext.Response.WriteAsJsonAsync(new
        //        {
        //            success = false,
        //            message = "Too many failed attempts or requests. Please try again later."
        //        }, token);
        //    };
        //});

        // 2. Identity Management Membership Strategy
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Password.RequireUppercase = true;
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 6;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // 3. Application Security Cookie Policies
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Home/Index";
            options.LogoutPath = "/Home/Logout";
            options.AccessDeniedPath = "/Home/AccessDenied";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // <-- Changed
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Name = "OnlineVotingApplicationAuth";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            options.SlidingExpiration = true;
        });

        services.ConfigureExternalCookie(options =>
        {
            options.Cookie.Name = "OnlineVotingApplicationExternalCookie";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // <-- Changed
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });

        return services;
    }
}
