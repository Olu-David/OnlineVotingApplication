using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Services;

namespace OnlineVotingApplication.Repository.BackGroundServices
        {
        public class VoteBackgroundService : BackgroundService
        {
            private readonly IServiceScopeFactory _scopeFactory;
            private readonly VotingChannel _channel;
            private readonly ILogger<VoteBackgroundService> _logger;
            private readonly int _pollIntervalMinutes = 2; // Database reconciliation interval

            public VoteBackgroundService(
                IServiceScopeFactory scopeFactory,
                VotingChannel channel,
                ILogger<VoteBackgroundService> logger)
            {
                _scopeFactory = scopeFactory;
                _channel = channel;
                _logger = logger;
            }

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _logger.LogInformation("🚀 Hybrid Vote Background Processing Engine Online.");

                // Run BOTH the real-time channel consumer and the DB safety-net poller concurrently
                var liveVotingTask = ProcessLiveVotesAsync(stoppingToken);
                var databasePollingTask = PollDatabaseForVotesAsync(stoppingToken);

                await Task.WhenAll(liveVotingTask, databasePollingTask);

                _logger.LogInformation("Vote background service is stopping cleanly.");
            }

            // TRACK A: Instant high-speed memory streaming pipeline via Channel
            private async Task ProcessLiveVotesAsync(CancellationToken stoppingToken)
            {
                await foreach (var voteJob in _channel.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var logger = scope.ServiceProvider.GetRequiredService<ILogger<VoteBackgroundService>>();

                        var existingVote = await dbContext.Votes
                            .FirstOrDefaultAsync(v => v.VoterId == voteJob.VoterId &&
                                                      v.ElectionId == voteJob.ElectionId &&
                                                      v.PositionId == voteJob.PositionId,
                                                      stoppingToken);

                        if (existingVote != null)
                        {
                            existingVote.HasVoted = true;
                            existingVote.IsConfirmed = true;
                            existingVote.CandidateId = voteJob.CandidateId;
                        }
                        else
                        {
                            var newVote = new Vote
                            {
                                Id = Guid.NewGuid(),
                                VoterId = voteJob.VoterId,
                                ElectionId = voteJob.ElectionId,
                                CandidateId = voteJob.CandidateId,
                                PositionId = voteJob.PositionId,
                                StateId = voteJob.StateId,
                                TenantId = voteJob.TenantId,
                                ConfirmationCode = voteJob.ConfirmationCode,
                                HasVoted = true,
                                IsConfirmed = true
                            };
                            await dbContext.Votes.AddAsync(newVote, stoppingToken);
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);
                        logger.LogInformation("Successfully committed live vote for Voter {VoterId} on Position {PositionId}",
                            voteJob.VoterId, voteJob.PositionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Critical routing crash detected inside Live Voting stream loop.");
                    }
                }
            }

            // TRACK B: Periodic recovery safety net loop (Catches uncommitted or orphaned votes)
            private async Task PollDatabaseForVotesAsync(CancellationToken stoppingToken)
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_pollIntervalMinutes));

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    _logger.LogInformation("🔍 [DATABASE POLL] Checking for uncommitted vote confirmations...");

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        // Look for votes that were confirmed via code but somehow didn't finalize HasVoted = true
                        var stuckVotes = await db.Votes
                            .Where(v => v.IsConfirmed && !v.HasVoted)
                            .Take(50)
                            .ToListAsync(stoppingToken);

                        if (stuckVotes.Count == 0) continue;

                        _logger.LogInformation("Found {Count} uncommitted vote entries. Reconciling...", stuckVotes.Count);

                        foreach (var vote in stuckVotes)
                        {
                            if (stoppingToken.IsCancellationRequested) break;
                            vote.HasVoted = true;
                        }

                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception pollEx)
                    {
                        _logger.LogError(pollEx, "Vote Poller framework loop ran into an error condition.");
                    }
                }
            }
        }
    }

