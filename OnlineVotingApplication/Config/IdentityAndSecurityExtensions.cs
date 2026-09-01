using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Repository.DatabaseService;

namespace OnlineVotingApplication.Config;

public static class IdentityAndSecurityExtensions
{
    public static IServiceCollection AddCustomIdentityAndSecurity(this IServiceCollection services)
    {
        // 1. DDoS and Brute Force Protection Engine (Rate Limiter)
        services.AddRateLimiter(options =>
        {
            options.AddSlidingWindowLimiter("StrictPolicy", opt =>
            {
                opt.PermitLimit = 5;
                opt.Window = TimeSpan.FromMinutes(30);
                opt.SegmentsPerWindow = 5;
                opt.QueueLimit = 0;
            });

            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "Too many failed attempts. For security, your 2FA verification is locked for 10 minutes."
                }, token);
            };
        });

        // 2. Identity Management Membership Strategy
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromDays(3);
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
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Name = "OnlineVotingApplicationAuth";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            options.SlidingExpiration = true;
        });

        return services;
    }
}
