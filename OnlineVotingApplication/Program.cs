using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.BackGroundServices;
using OnlineVotingApplication.Repository.DatabaseService;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using OnlineVotingApplication.Repository.Settings;

namespace OnlineVotingApplication
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ==========================================
            // 1. DATABASE CONNECTION
            // ==========================================
            var connectionString = builder.Configuration.GetConnectionString("OnlineVotingApplicationContextConnection")
                ?? throw new InvalidOperationException("Connection string not found.");

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString));

            // ==========================================
            // 2. SERVICE REGISTRATIONS
            // ==========================================
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddControllersWithViews();

            // Email settings
            builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

            // Background workers
            builder.Services.AddHostedService<NotificationBackgroundService>();
            builder.Services.AddHostedService<FileProcessingBackgroundService>();
            //builder.Services.AddHostedService<EmailBackgroundService>();

            // Messaging channels
            builder.Services.AddSingleton<NotificationChannel>();
            builder.Services.AddSingleton<FileChannel>();
            //builder.Services.AddSingleton<EmailQueue>();
            //builder.Services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueue>());

            // Scoped application services
            builder.Services.AddScoped<iAuthService, AuthService>();
            builder.Services.AddScoped<iCandidateService, CandidateService>();
            builder.Services.AddScoped<iFileService, FileService>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<IElectionService, ElectionService>();
            builder.Services.AddScoped<IVoteService, VoteService>();

            // ==========================================
            // 3. RATE LIMITING
            // ==========================================
            builder.Services.AddRateLimiter(options =>
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

            // ==========================================
            // 4. IDENTITY CONFIGURATION
            // ==========================================
            builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromDays(3);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Password.RequireUppercase = true;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 6;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

            // ==========================================
            // 5. COOKIE SETTINGS
            // ==========================================
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Home/Index";
                options.LogoutPath = "/Home/Logout";
                options.AccessDeniedPath = "/Home/AccessDenied";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Name = "OnlineVotingApplication";

                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = true;
            });

            // ==========================================
            // 6. BUILD THE APPLICATION
            // ==========================================
            var app = builder.Build();

            // ==========================================
            // 7. DATABASE INITIALIZATION & SEEDING
            // ==========================================
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    var context = services.GetRequiredService<AppDbContext>();
                    //var emailQueue = services.GetRequiredService<EmailQueue>();

                    logger.LogInformation("Applying database migrations...");
                    await context.Database.MigrateAsync();
                    Console.WriteLine("Seeder started");
                    logger.LogInformation("Seeding roles and users...");
                    await DbroleSeeder.SeedRolesAndUsersAsync(services);
                    Console.WriteLine("Seeder finished");
                    logger.LogInformation("Seeder is running");
                    if (!await context.PendingEmails.AnyAsync())
                    {
                        logger.LogInformation("Seeding test emails...");
                        context.PendingEmails.AddRange(
                            new PendingEmail { Recipient = "test1@example.com", Subject = "Welcome", Body = "<h1>Hello World!</h1>" },
                            new PendingEmail { Recipient = "test2@example.com", Subject = "Offer", Body = "<p>50% off today!</p>" }
                        );
                        await context.SaveChangesAsync();
                    }

                    logger.LogInformation("Recovering pending emails into the processing queue...");
                    //await emailQueue.RecoverPendingEmailsAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "System initialization failed.");
                }
            }

            // ==========================================
            // 8. MIDDLEWARE PIPELINE
            // ==========================================
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // ==========================================
            // 9. RUN THE APPLICATION
            // ==========================================
            app.Run();
        }
    }
}