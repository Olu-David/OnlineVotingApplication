using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.Repository.BackGroundServices;
using OnlineVotingApplication.Repository.DatabaseService;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using OnlineVotingApplication.Repository.Settings;
using OnlineVotingApplication.Services;

namespace OnlineVotingApplication.Config;

public static class VotingInfrastructureExtensions
{
    public static IServiceCollection AddVotingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Relational Database Configuration
        if (!services.Any(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)))
        {
            var connectionString = configuration.GetConnectionString("OnlineVotingApplicationContextConnection")
                ?? throw new InvalidOperationException("Database connection string 'OnlineVotingApplicationContextConnection' is missing.");

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOptions.CommandTimeout(60);
                }));
        }

        // 2. Framework Utilities
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddControllersWithViews();

        // 3. Strongly-Typed Options Settings Mapping
        services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));

        // 4. Thread-Safe Messaging Channels (Singletons)
        services.AddSingleton<NotificationChannel>();
        services.AddSingleton<FileChannel>();
        services.AddSingleton<VotingChannel>();
        services.AddSingleton<DeleteChannel>();

        // 5. Asynchronous Background Hosted Services
        services.AddHostedService<NotificationBackgroundService>();
        services.AddHostedService<FileProcessingBackgroundService>();
        services.AddHostedService<VoteBackgroundService>();
        services.AddHostedService<DeleteBackGroundService>();

        // 6. Core Domain Business Services (Scoped Dependency Injection)
        services.AddScoped<iAuthService, AuthService>();
        services.AddScoped<iCandidateService, CandidateService>();
        services.AddScoped<iFileService, FileService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IElectionService, ElectionService>();
        services.AddScoped<IVoteService, VoteService>();
        services.AddScoped<iPositionService, PositionService>();
        services.AddScoped<iStateService, StateService>();
        services.AddScoped<iLgaService, LgaService>();
        services.AddScoped<IPartyService, PartyService>();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<ISupportService, SupportService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<HybridFormBuilderService>();

        // 7. SignalR
        services.AddSignalR();

        return services;
    }
}