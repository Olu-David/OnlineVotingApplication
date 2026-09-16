using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.Repository.BackGroundServices;
using OnlineVotingApplication.Repository.DatabaseService;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using OnlineVotingApplication.Repository.Settings;
using OnlineVotingApplication.Services;
using OnlineVotingApplication.SupaBase;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.Runtime;

namespace OnlineVotingApplication.Config;

public static class VotingInfrastructureExtensions
{
    public static IServiceCollection AddVotingInfrastructure(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var providerConfig = configuration["DatabaseProvider"];
        bool forcePostgres = !string.IsNullOrEmpty(providerConfig) && providerConfig.Equals("Postgres", StringComparison.OrdinalIgnoreCase);

        // Fixed: Directly target DefaultConnection/DATABASE_URL first, bypassing local connection overrides
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? configuration["DATABASE_URL"]
                               ?? configuration.GetConnectionString("LocalConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string is null or empty! Ensure DATABASE_URL or DefaultConnection is configured.");
        }

        // Sanitize and strip out accidental tcp:// prefix on the WHOLE string
        if (connectionString.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = connectionString.Substring(6);
        }

        // Strip any scheme prefix immediately after "Host="
        connectionString = Regex.Replace(
            connectionString,
            @"(Host\s*=\s*)(?:tcp|https?|postgres(?:ql)?)://",
            "$1",
            RegexOptions.IgnoreCase);

        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var queryIndex = connectionString.IndexOf('?');
            if (queryIndex >= 0)
            {
                connectionString = connectionString.Substring(0, queryIndex);
            }

            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            {
                var userInfo = uri.UserInfo.Split(':');
                var dbName = uri.AbsolutePath.TrimStart('/');

                connectionString = $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={dbName};Username={userInfo[0]};Password={(userInfo.Length > 1 ? userInfo[1] : "")};SSL Mode=Require;Trust Server Certificate=true;";
            }
            else
            {
                throw new InvalidOperationException($"The connection string starts with a postgres protocol, but the URI is malformed and cannot be parsed: '{connectionString}'");
            }
        }

        bool usePostgres = forcePostgres ||
            !string.IsNullOrEmpty(providerConfig) && providerConfig.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("postgres://", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(providerConfig))
        {
            bool looksLikeSqlServer = connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                                      connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase);

            if (!usePostgres && !looksLikeSqlServer)
            {
                throw new InvalidOperationException("Could not determine database provider from connection string. Set 'DatabaseProvider' explicitly.");
            }
        }

        services.AddDbContext<AppDbContext>(options =>
        {
            if (usePostgres)
            {
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                    npgsql.CommandTimeout(60);
                })
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
            }
            else
            {
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                    sql.CommandTimeout(60);
                });
            }
        });

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddControllersWithViews();

        services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
        services.AddSingleton<NotificationChannel>();
        services.AddSingleton<FileChannel>();
        services.AddSingleton<VotingChannel>();
        services.AddSingleton<DeleteChannel>();

        services.AddHostedService<NotificationBackgroundService>();
        services.AddHostedService<FileProcessingBackgroundService>();
        services.AddHostedService<VoteBackgroundService>();
        services.AddHostedService<DeleteBackGroundService>();

        // ═══════════════════════════════════════════════════════════
        // SUPABASE CLIENT — validated at startup
        // ═══════════════════════════════════════════════════════════
        services.AddScoped<Supabase.Client>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();

            var url = (config["Supabase:Url"] ?? config["SupabaseUrl"] ?? "").Trim();
            var key = (config["Supabase:ServiceRoleKey"] ?? config["Supabase:Key"] ?? config["SupabaseKey"] ?? "").Trim();

            // 1. Null / empty checks
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException(
                    "Supabase:Url is missing. Set the environment variable Supabase__Url on your host " +
                    "or fill it in appsettings.json.");

            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException(
                    "Supabase:ServiceRoleKey is missing. Set the environment variable Supabase__ServiceRoleKey.");

            // 2. Placeholder detection (${...} template syntax)
            if (url.StartsWith("$") || url.Contains("${"))
                throw new InvalidOperationException(
                    $"Supabase:Url contains an unresolved placeholder: '{url}'. " +
                    "Remove the ${{...}} syntax from appsettings.json and set the real env var instead.");

            if (key.StartsWith("$") || key.Contains("${"))
                throw new InvalidOperationException(
                    "Supabase:ServiceRoleKey contains an unresolved placeholder. Set the real env var.");

            // 3. URL sanity
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Supabase:Url must start with https://. Got: '{url}'");

            url = url.TrimEnd('/'); // remove trailing slash

            // 4. Key sanity
            if (!key.StartsWith("eyJ", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Supabase:ServiceRoleKey does not look like a JWT (should start with 'eyJ'). " +
                    "Make sure you copied the service_role secret, not the anon key.");

            return new Supabase.Client(url, key, new Supabase.SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true
            });
        });
        // ═══════════════════════════════════════════════════════════
        // AWS S3 CLIENT (Supabase Storage S3 Protocol)
        // ═══════════════════════════════════════════════════════════
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var awsSection = config.GetSection("AWS");

            var serviceUrl = awsSection["ServiceUrl"] ?? config["AWS_SERVICE_URL"];
            var accessKey = awsSection["AccessKey"] ?? config["AWS_ACCESS_KEY_ID"];
            var secretKey = awsSection["SecretKey"] ?? config["AWS_SECRET_ACCESS_KEY"];

            if (string.IsNullOrWhiteSpace(serviceUrl) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                throw new InvalidOperationException("AWS S3 settings (ServiceUrl, AccessKey, SecretKey) are missing for Supabase S3 storage.");
            }

            var s3Config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true // Required for Supabase S3 compatibility
            };

            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            return new AmazonS3Client(credentials, s3Config);
        });

        services.AddScoped<iAuthService, AuthService>();
        services.AddScoped<iCandidateService, CandidateService>();
        services.AddScoped<ISupaBaseFileService, SupabaseFileService>();
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
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<iFileService, FileService>();
        services.AddScoped<IAppleAuthService, AppleAuthService>();
        services.AddScoped<iExternalAuthService, ExternalAuthService>();
        services.AddScoped<HybridFormBuilderService>();

        // Store data protection keys in your Supabase PostgreSQL database
        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>();

        services.AddSignalR();

        var redisConnectionString = configuration["REDIS_URL"] ?? configuration.GetConnectionString("RedisConnection");

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = !string.IsNullOrWhiteSpace(redisConnectionString) ? redisConnectionString : "localhost:6379";
            options.InstanceName = "VotezyCache_";
        });

        return services;
    }
}