using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.DatabaseService;

namespace OnlineVotingApplication.Config;

public static class DatabaseMigrationExtensions
{
    public static async Task InitializeAndSeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetService<ILogger<Program>>();

        try
        {
            var context = services.GetRequiredService<AppDbContext>();

            // 1. In-Memory execution path for testing environment
            if (context.Database.IsInMemory())
            {
                await context.Database.EnsureCreatedAsync();
                return;
            }

            // 2. Safely check if a connection string is present
            var configuration = services.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger?.LogWarning("[DATABASE WARNING] 'DefaultConnection' string is missing or empty. Skipping migrations and seeding.");
                Console.WriteLine("[DATABASE WARNING] 'DefaultConnection' string is missing or empty. Skipping migrations and seeding.");
                return;
            }

            // Safe metadata extraction (prevents provider formatting crashes)
            string dbName = "Unknown Database";
            string dataSource = "Unknown Host/Server";

            try
            {
                var dbConnection = context.Database.GetDbConnection();
                dbName = dbConnection?.Database ?? "Unknown Database";
                dataSource = dbConnection?.DataSource ?? "Unknown Host/Server";
            }
            catch
            {
                dataSource = context.Database.ProviderName ?? "Unknown Provider";
            }

            Console.WriteLine($"[DATABASE CHECK] Attempting connection to Server/Provider: '{dataSource}' | Database: '{dbName}'...");
            logger?.LogInformation("[DATABASE CHECK] Attempting connection to Server/Provider: '{DataSource}' | Database: '{DbName}'", dataSource, dbName);

            // 3. Fast Pre-Flight Connection Check
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
                logger?.LogWarning("[DATABASE TIMEOUT] Pre-flight connection timed out after 5 seconds. Skipping migrations and seeding.");
                Console.WriteLine("[DATABASE TIMEOUT] Pre-flight connection timed out after 5 seconds. Skipping migrations and seeding.");
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