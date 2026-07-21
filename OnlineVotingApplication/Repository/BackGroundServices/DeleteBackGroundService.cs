using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Repository.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.BackGroundServices
{
    public class DeleteBackGroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<DeleteBackGroundService> _logger;

        // The engine wakes up and runs a database sweep once every 24 hours
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public DeleteBackGroundService(IServiceScopeFactory serviceScopeFactory, ILogger<DeleteBackGroundService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🧹 30-Day Permanent Deletion Cleanup Engine Online.");

            using var timer = new PeriodicTimer(_checkInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation("🔍 Scanning database for soft-deleted records older than 30 days...");

                try
                {
                    await PerformScheduledCleanupAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical failure during scheduled 30-day deletion sweep.");
                }
            }
        }

        private async Task PerformScheduledCleanupAsync(CancellationToken cancellationToken)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Records soft-deleted before this exact timestamp are expired and will be permanently purged
                var expirationThreshold = DateTime.UtcNow.AddDays(-30);

                // --- 1. PURGE CANDIDATES ---
                var expiredCandidates = await db.Candidate
                    .Where(c => c.isDeleted && c.DeletedAt <= expirationThreshold)
                    .ToListAsync(cancellationToken);

                if (expiredCandidates.Any())
                {
                    foreach (var candidate in expiredCandidates)
                    {
                        try
                        {
                            db.Candidate.Remove(candidate);
                            _logger.LogInformation($"Permanently purged candidate: {candidate.Name} (ID: {candidate.Id})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to permanently purge candidate ID: {candidate.Id}");
                        }
                    }
                }

                // --- 2. PURGE POSITIONS ---
           
                var expiredPositions = await db.Position
                    .Where(p => p.IsDeleted && p.DeletedAt <= expirationThreshold)
                    .ToListAsync(cancellationToken);

                if (expiredPositions.Any())
                {
                    foreach (var position in expiredPositions)
                    {
                        try
                        {
                            db.Position.Remove(position);
                            _logger.LogInformation($"Permanently purged position from database ID: {position.Id}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to permanently purge position ID: {position.Id}");
                        }
                    }
                }

                // --- 3. PURGE LGAs ---
                var expiredLgas = await db.Lgas
                    .Where(l => l.IsDeleted && l.DeletedAt <= expirationThreshold)
                    .ToListAsync(cancellationToken);

                if (expiredLgas.Any())
                {
                    foreach (var lga in expiredLgas)
                    {
                        try
                        {
                            db.Lgas.Remove(lga);
                            _logger.LogInformation($"Permanently purged LGA from database ID: {lga.Id}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to permanently purge LGA ID: {lga.Id}");
                        }
                    }
                }

                // --- 4. PURGE STATES ---
                var expiredStates = await db.States
                    .Where(s => s.IsDeleted && s.DeletedAt <= expirationThreshold)
                    .ToListAsync(cancellationToken);

                if (expiredStates.Any())
                {
                    foreach (var state in expiredStates)
                    {
                        try
                        {
                            db.States.Remove(state);
                            _logger.LogInformation($"Permanently purged State from database ID: {state.Id}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to permanently purge State ID: {state.Id}");
                        }
                    }
                }

                // Save all removals to the database in a single thread-safe batch transaction
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
