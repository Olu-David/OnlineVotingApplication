using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.DatabaseService;
using System.Text.RegularExpressions;

namespace OnlineVotingApplication.Config
{
    public static class DatabaseMigrationExtensions
    {
        public static async Task InitializeAndSeedDatabaseAsync(this WebApplication app, IWebHostEnvironment environment)
        {
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            var logger = services.GetService<ILogger<Program>>();

            try
            {
                var configuration = services.GetRequiredService<IConfiguration>();

                // FIXED: Now checks DefaultConnection, DATABASE_URL, and LocalConnection uniformly
                var connectionString = configuration.GetConnectionString("DefaultConnection")
                                    ?? configuration["DATABASE_URL"]
                                    ?? configuration.GetConnectionString("LocalConnection");

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    logger?.LogWarning("[DATABASE WARNING] Connection string is missing or empty. Skipping migrations and seeding.");
                    Console.WriteLine("[DATABASE WARNING] Connection string is missing or empty. Skipping migrations and seeding.");
                    return;
                }

                // Clean up tcp:// prefix if present
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

                // Handle Supabase/Postgres URI format if passed directly
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
                }

                // FORCE the DbContext to use the clean configuration string
                var context = services.GetRequiredService<AppDbContext>();
                context.Database.SetConnectionString(connectionString);

                // 1. In-Memory execution path for testing environment
                if (context.Database.IsInMemory())
                {
                    await context.Database.EnsureCreatedAsync();
                    return;
                }

                // Safe metadata extraction
                string dbNameMeta = "Unknown Database";
                string dataSource = "Unknown Host/Server";

                try
                {
                    var dbConnection = context.Database.GetDbConnection();
                    dbNameMeta = dbConnection?.Database ?? "Unknown Database";
                    dataSource = dbConnection?.DataSource ?? "Unknown Host/Server";
                }
                catch
                {
                    dataSource = context.Database.ProviderName ?? "Unknown Provider";
                }

                Console.WriteLine($"[DATABASE CHECK] Attempting connection to Server/Provider: '{dataSource}' | Database: '{dbNameMeta}'...");
                logger?.LogInformation("[DATABASE CHECK] Attempting connection to Server/Provider: '{DataSource}' | Database: '{DbName}'", dataSource, dbNameMeta);

                // 3. Pre-Flight Connection Check (Extended to 30s for Cold Starts)
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    bool canConnect = await context.Database.CanConnectAsync(cts.Token);

                    if (!canConnect)
                    {
                        logger?.LogWarning("[DATABASE WARNING] Database server at '{DataSource}' is unreachable. Skipping migrations and seeding.", dataSource);
                        Console.WriteLine($"[DATABASE WARNING] Database server at '{dataSource}' is unreachable. Skipping migrations and seeding.");
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    logger?.LogWarning("[DATABASE TIMEOUT] Pre-flight connection timed out after 30 seconds. Skipping migrations and seeding.");
                    Console.WriteLine("[DATABASE TIMEOUT] Pre-flight connection timed out after 30 seconds. Skipping migrations and seeding.");
                    return;
                }

                // 4. Execute Migrations
                logger?.LogInformation("Connection verified. Applying pending database migrations...");
                await context.Database.MigrateAsync();

                // 5. Execute Role and User Seeders
                logger?.LogInformation("Beginning system role and user population sequence...");
                Console.WriteLine("Beginning system role and user population sequence...");
                await DbroleSeeder.SeedRolesAndUsersAsync(services);

                // 6. Stage Mock System Emails
                if (context.PendingEmails != null && !await context.PendingEmails.AnyAsync())
                {
                    logger?.LogInformation("Injecting fresh mock staging system emails...");
                    context.PendingEmails.AddRange(
                        new PendingEmail { Recipient = "test1@example.com", Subject = "Welcome", Body = "<h1>Hello World!</h1>" },
                        new PendingEmail { Recipient = "test2@example.com", Subject = "Offer", Body = "<p>50% off today!</p>" }
                    );
                    await context.SaveChangesAsync();
                    logger?.LogInformation("Mock system emails successfully staged.");
                }

                logger?.LogInformation("System database lifecycle initializations finished smoothly.");
                Console.WriteLine("System database lifecycle initializations finished smoothly.");
            }
            catch (Exception ex)
            {
                logger?.LogCritical(ex, "An unrecoverable system exception occurred during core migration or data seeding routines.");
                Console.WriteLine($"[CRITICAL FAILURE] Database Configuration Routine Interrupted: {ex.Message}");
                throw;
            }
        }
    }
}