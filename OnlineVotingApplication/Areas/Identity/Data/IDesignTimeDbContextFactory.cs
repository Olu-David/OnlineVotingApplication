using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OnlineVotingApplication.Areas.Identity.Data;
using System.IO;

namespace OnlineVotingApplication.Config
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // Build configuration from appsettings.json so tooling can find the connection string
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var builder = new DbContextOptionsBuilder<AppDbContext>();

            // Hardcode or retrieve the Render/PostgreSQL connection string
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                                   ?? configuration["ConnectionStrings:DefaultConnection"]
                                   ?? "postgresql://stickzz_man:YlZiyXvc7QQjaDJ8NFliZjYRrrGBrIeD@dpg-dae0hcv40ujc73d9milg-a.frankfurt-postgres.render.com/votezy_db_0mb1";

            // Safely parse URI if it starts with postgresql://
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
                    var port = uri.Port > 0 ? uri.Port : 5432;

                    connectionString = $"Host={uri.Host};Port={port};Database={dbName};Username={userInfo[0]};Password={(userInfo.Length > 1 ? userInfo[1] : "")};SSL Mode=Require;Trust Server Certificate=true;";
                }
            }

            // FORCE PostgreSQL (Npgsql) - This completely blocks SQL Server fallback
            builder.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                npgsql.CommandTimeout(60);
            });

            return new AppDbContext(builder.Options, null!);
        }
    }
}
