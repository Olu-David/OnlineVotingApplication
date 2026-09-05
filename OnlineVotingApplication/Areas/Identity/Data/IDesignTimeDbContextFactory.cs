using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace OnlineVotingApplication.Areas.Identity.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<AppDbContext>()
            .AddEnvironmentVariables()
            .Build();

        // Directly target the connection string or fallback to environment variables
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                               ?? Environment.GetEnvironmentVariable("DefaultConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Could not find 'DefaultConnection' in configuration or User Secrets.");
        }

        // ─── EXPLICIT URI PARSER FOR NPGSQL ──────────────────────────────────
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
            else
            {
                throw new InvalidOperationException($"Malformed PostgreSQL URI: '{connectionString}'");
            }
        }

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseNpgsql(connectionString);

        return new AppDbContext(builder.Options, null!);
    }
}