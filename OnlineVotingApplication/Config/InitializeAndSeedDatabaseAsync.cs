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
        var logger = services.GetRequiredService<ILogger<Program>>();

        try
        {
            var context = services.GetRequiredService<AppDbContext>();

            if (context.Database.IsInMemory())
            {
                await context.Database.EnsureCreatedAsync();
                return;
            }

            var dbConnection = context.Database.GetDbConnection();
            var dbName = dbConnection.Database;
            var dataSource = dbConnection.DataSource;

            Console.WriteLine($"[DATABASE CHECK] Connected to Server: '{dataSource}' | Database Name: '{dbName}'");
            logger.LogInformation("[DATABASE CHECK] Connected to Server: '{DataSource}' | Database Name: '{DbName}'", dataSource, dbName);

            await context.Database.MigrateAsync();

            logger.LogInformation("Beginning system role and user population sequence...");
            Console.WriteLine("Beginning system role and user population sequence...");

            await DbroleSeeder.SeedRolesAndUsersAsync(services);

            if (!await context.PendingEmails.AnyAsync())
            {
                logger.LogInformation("Injecting fresh mock staging system emails...");
                context.PendingEmails.AddRange(
                    new PendingEmail { Recipient = "test1@example.com", Subject = "Welcome", Body = "<h1>Hello World!</h1>" },
                    new PendingEmail { Recipient = "test2@example.com", Subject = "Offer", Body = "<p>50% off today!</p>" }
                );
                await context.SaveChangesAsync();
                logger.LogInformation("Mock system emails successfully staged.");
            }

            logger.LogInformation("System database lifecycle initializations finished smoothly.");
            Console.WriteLine("System database lifecycle initializations finished smoothly.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An unrecoverable system exception happened during core migration or data seeding routines.");
            Console.WriteLine($"[CRITICAL FAILURE] Database Configuration Routine Interrupted: {ex.Message}");
            throw;
        }
    }
}
