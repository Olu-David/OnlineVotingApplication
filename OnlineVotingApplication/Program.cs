using AspNetCoreGeneratedDocument;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.BackGroundServices;
using OnlineVotingApplication.Repository.DatabaseService;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using System;

namespace OnlineVotingApplication
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var connectionString = builder.Configuration.GetConnectionString("OnlineVotingApplicationContextConnection") ?? throw new InvalidOperationException("Connection string 'OnlineVotingApplicationContextConnection' not found.");;

            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

            //Background Service
            builder.Services.AddHostedService<NotificationBackgroundService>();
            builder.Services.AddHostedService<FileProcessingBackgroundService>();

            // Register the thread-safe memory channel as a singleton
            builder.Services.AddSingleton<NotificationChannel>();
            builder.Services.AddSingleton<FileChannel>();


            builder.Services.AddRateLimiter(options =>
            {
                // 🛡️ SECURITY POLICY FOR 2FA CODES
                options.AddSlidingWindowLimiter("StrictPolicy", opt =>
                {
                    opt.PermitLimit = 5;                        // Allow max 5 verification attempts
                    opt.Window = TimeSpan.FromMinutes(30);     // Inside a rolling 10-minute window
                    opt.SegmentsPerWindow = 5;                 // Smooths out the evaluation interval
                    opt.QueueLimit = 0;                        // Reject instantly if exceeded
                });

                // Custom Response Handler
                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json";

                    var errorObj = new
                    {
                        success = false,
                        message = "Too many failed attempts. For security, your 2FA verification is locked for 10 minutes."
                    };
                    await context.HttpContext.Response.WriteAsJsonAsync(errorObj, token);
                };
            });

            //Identity 
            builder.Services.AddIdentity<ApplicationUser, IdentityRole>(option =>
            {
                option.SignIn.RequireConfirmedEmail= true;
                option.Password.RequireNonAlphanumeric = true;
                option.Lockout.DefaultLockoutTimeSpan= TimeSpan.FromDays(3);
                option.Lockout.MaxFailedAccessAttempts= 5;
                option.Password.RequireUppercase = true;
                option.User.RequireUniqueEmail = true;
                option.Password.RequiredLength = 6;
                

            }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
            builder.Services.AddScoped<iCandidateService, CandidateService>();
             builder.Services.AddScoped<IElectionService, ElectionService>();
             builder.Services.AddScoped<IVoteService, VoteService>();
            builder.Services.AddScoped<iFileService, FileService>();

             //builder.Services.AddScoped<IResultService, ResultService>();
            //Cookie Settings
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Home/Index";
                options.LogoutPath = "/Home/Logout";
                options.AccessDeniedPath = "/Home/AccessDenied";
                options.Cookie.HttpOnly = true;
                // Changed to SameAsRequest so it works on http://localhost
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Name = "Online Voting Application";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = true;
            });
            
            //Add HttpAccessor 
            builder.Services.AddHttpContextAccessor();

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            var app = builder.Build();
         
            //DbRoleInitializer
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    // ADD THIS LINE: This creates the DB and applies any pending migrations
                    var context = services.GetRequiredService<AppDbContext>();
                    await context.Database.MigrateAsync();

                    await DbroleSeeder.SeedRolesAsync(services);



                    // Seed test emails if the queue is empty
                    if (!await context.PendingEmails.AnyAsync())
                    {
                        context.PendingEmails.AddRange(
                            new PendingEmail { Recipient = "test1@example.com", Subject = "Welcome", Body = "<h1>Hello World!</h1>" },
                            new PendingEmail { Recipient = "test2@example.com", Subject = "Offer", Body = "<p>50% off today!</p>" }
                        );
                        await context.SaveChangesAsync();
                        Console.WriteLine("✅ Seeded 2 test emails.");
                    }
                }
                catch (Exception ex)
                {
                    // Log the actual error to the console so you can see why it's failing
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "An error occurred during database migration or seeding.");
                }
            }
            //add Memory Cache
            builder.Services.AddMemoryCache();

          
            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
                
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            app.Run();
        }
    }
}
