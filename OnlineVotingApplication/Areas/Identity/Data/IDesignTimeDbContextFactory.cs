using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OnlineVotingApplication.Areas.Identity.Data;
using System;
using System.IO;

namespace OnlineVotingApplication.Config
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // Build configuration to properly read from appsettings and user secrets
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddUserSecrets<DesignTimeDbContextFactory>()
                .Build();

            var builder = new DbContextOptionsBuilder<AppDbContext>();

            // Pull the connection string dynamically from configuration/secrets
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            builder.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                npgsql.CommandTimeout(120);
            });

            return new AppDbContext(builder.Options, null!);
        }
    }
}