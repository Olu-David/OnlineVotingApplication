using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Jobs;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Settings;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.BackGroundServices
{
    public class NotificationBackgroundService : BackgroundService
    {
        private readonly ILogger<NotificationBackgroundService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NotificationChannel _notificationChannel;
        private readonly int _checkIntervalMinutes;

        public NotificationBackgroundService(ILogger<NotificationBackgroundService> logger, IServiceScopeFactory scopeFactory, NotificationChannel notificationChannel)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _notificationChannel = notificationChannel;
            _checkIntervalMinutes = 5; // Polling fallback interval
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Email Background Service initialized with Hybrid architecture.");

            // Start BOTH streams simultaneously on separate task threads
            var liveEmailStreamTask = ProcessLiveEmailsAsync(stoppingToken);
            var databasePollingTask = PollDatabaseForEmailsAsync(stoppingToken);

            await Task.WhenAll(liveEmailStreamTask, databasePollingTask);

            _logger.LogInformation("Email background service is stopping.");
        }

        // TRACK A: Processes real-time emails instantly from the memory channel queue
        private async Task ProcessLiveEmailsAsync(CancellationToken stoppingToken)
        {
            while (await _notificationChannel.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_notificationChannel.Reader.TryRead(out var notificationJob))
                {
                    try
                    {
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            // 1. Check the Notification Type before processing
                            if (notificationJob.Type == NotificationType.Email)
                            {
                                _logger.LogInformation("📧 [LIVE CHANNEL] Processing instant email for {Recipient}", notificationJob.To);

                                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                                await emailService.EmailSendAsync(notificationJob.To, notificationJob.Subject, notificationJob.Body, stoppingToken);
                            }
                            else if (notificationJob.Type == NotificationType.Sms)
                            {
                                _logger.LogInformation("📱 [LIVE CHANNEL] Processing instant 2FA SMS code for {Recipient}", notificationJob.To);

                                // In production: var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();
                                // await smsService.SendSmsAsync(notificationJob.To, notificationJob.Body, stoppingToken);

                                await Task.Delay(100, stoppingToken); // Mocking SMS api gateway delay
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Live notification routing failed for {Recipient}. Offloading to database recovery.", notificationJob.To);
                        await SaveFailedEmailToDbAsync(notificationJob);
                    }
                }
            }
        }

        // TRACK B: Polls your Database table every 5 minutes to catch stuck/scheduled items
        private async Task PollDatabaseForEmailsAsync(CancellationToken stoppingToken)
        {
            // PeriodicTimer handles background tracking cleanly without thread overlapping
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_checkIntervalMinutes));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation("🔍 [DATABASE POLL] Scanning database table for pending notifications...");

                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        // 1. Fetch records that haven't sent AND haven't exceeded a safe retry threshold (e.g., max 3 times)
                        var pendingNotifications = await db.PendingEmails
                            .Where(e => !e.IsSent && e.RetryCount < 3)
                            .ToListAsync(stoppingToken);

                        if (pendingNotifications.Count == 0) continue;

                        _logger.LogInformation("Found {Count} stuck or scheduled pending items.", pendingNotifications.Count);

                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                        foreach (var item in pendingNotifications)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            // Increment retry attempts immediately to protect against infinite crash looping
                            item.RetryCount++;

                            try
                            {
                                // 2. Polymorphic Routing: Check what channel this record belongs to
                                if (item.Type == NotificationType.Email)
                                {
                                    await emailService.EmailSendAsync(item.Recipient, item.Subject, item.Body, stoppingToken);
                                }
                                else if (item.Type == NotificationType.Sms)
                                {
                                    _logger.LogInformation("📱 [POLLER RETRY] Dispatching failed SMS token to: {Phone}", item.Recipient);
                                }

                                // Mark as completely cleared on success
                                item.IsSent = true;
                            }
                            catch (OperationCanceledException)
                            {
                                _logger.LogWarning("Shutdown detected, stopping background notification batch processing.");
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed retry attempt ({Count}/3) for {Recipient}.", item.RetryCount, item.Recipient);
                            }

                            // 3. Save Changes Per-Item: Protects against bulk duplication if the app unexpectedly shuts down mid-loop
                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical error in background database notification processing loop.");
                }
            }
        }

        // Upgraded Recovery Fallback Handler (Completed with clean closing brackets!)
        private async Task SaveFailedEmailToDbAsync(NotificationJob notificationJob)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Look for an existing pending email entry to flag as failed or create a new recovery entry
                    var failedRecord = await db.PendingEmails
                        .FirstOrDefaultAsync(e => e.Recipient == notificationJob.To && !e.IsSent);

                    if (failedRecord != null)
                    {
                        failedRecord.RetryCount++;
                        await db.SaveChangesAsync();
                        _logger.LogInformation("Updated database tracking stats for failed live notification to: {Recipient}", notificationJob.To);
                    }
                } 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical failure writing fallback log tracker entry for email target: {Recipient}", notificationJob.To);
            }
        }
    }
}
