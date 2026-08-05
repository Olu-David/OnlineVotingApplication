using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OnlineVotingApplication.Jobs;

namespace OnlineVotingApplication.Repository.BackGroundServices
{
    public class FileProcessingBackgroundService : BackgroundService
    {
        private readonly ILogger<FileProcessingBackgroundService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly FileChannel _fileChannel;
        private readonly IWebHostEnvironment _env;
        private readonly int _checkIntervalMinutes = 5;

        public FileProcessingBackgroundService(
            ILogger<FileProcessingBackgroundService> logger,
            IServiceScopeFactory scopeFactory,
            FileChannel fileChannel,
            IWebHostEnvironment env)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _fileChannel = fileChannel;
            _env = env;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Hybrid File Background Processing Engine Online.");

            // Start BOTH channels simultaneously on separate concurrent processing lanes


            var liveFileStreamTask = ProcessLiveFilesAsync(stoppingToken);
            var databasePollingTask = PollDatabaseForFilesAsync(stoppingToken);

            await Task.WhenAll(liveFileStreamTask, databasePollingTask);
            _logger.LogInformation("File background service is stopping cleanly.");
        }

        // TRACK A: Instant high-speed memory streaming pipeline
        private async Task ProcessLiveFilesAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _fileChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var fileService = scope.ServiceProvider.GetRequiredService<iFileService>();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        switch (job)
                        {
                            // This block now safely processes BOTH optimized images AND standard documents (PDFs)
                            case ProcessImageJob img:
                                await UpdateStatusAsync(db, img.FileId, FileProcessingStatus.Processing, null, stoppingToken);
                                try
                                {
                                    // This now safely identifies files and skips ImageSharp if it's a PDF
                                    await fileService.RunImageOptimizationAsync(img.FileId, img.FilePath, stoppingToken);
                                    await UpdateStatusAsync(db, img.FileId, FileProcessingStatus.Completed, null, stoppingToken);
                                }
                                catch (Exception ex)
                                {
                                    await UpdateStatusAsync(db, img.FileId, FileProcessingStatus.Failed, ex.Message, stoppingToken);
                                }
                                break;

                            case ChunkVideoJob vid:
                                await UpdateStatusAsync(db, vid.FileId, FileProcessingStatus.Processing, null, stoppingToken);
                                try
                                {
                                    await fileService.RunVideoChunkingAsync(vid.FileId, vid.FilePath, vid.OutPutFolder, stoppingToken);
                                    await UpdateStatusAsync(db, vid.FileId, FileProcessingStatus.Completed, null, stoppingToken);
                                }
                                catch (OperationCanceledException)
                                {
                                    await UpdateStatusAsync(db, vid.FileId, FileProcessingStatus.Failed, "Aborted due to webserver shutdown.", stoppingToken);
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    await UpdateStatusAsync(db, vid.FileId, FileProcessingStatus.Failed, ex.Message, stoppingToken);
                                }
                                break;
                        }
                    }
                }
                catch (Exception rootEx)
                {
                    _logger.LogError(rootEx, "Critical routing crash detected inside Live Processing stream loop.");
                }
            }
        }


        // TRACK B: Periodic recovery safety net loop (Matches your email architecture!)
        private async Task PollDatabaseForFilesAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_checkIntervalMinutes));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation("🔍 [DATABASE POLL] Looking for orphan or skipped pending file transactions...");

                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var fileService = scope.ServiceProvider.GetRequiredService<iFileService>();

                        // Fetch records that got stuck or failed and have remaining retry tickets left
                        var stuckRecords = await db.PendingFiles
                            .Where(f => f.Status != FileProcessingStatus.Completed && f.RetryCount < 3)
                            .ToListAsync(stoppingToken);

                        if (stuckRecords.Count == 0) continue;

                        _logger.LogInformation("Found {Count} orphaned file processing lines. Executing serial fallback recovery...", stuckRecords.Count);

                        foreach (var record in stuckRecords)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            record.RetryCount++;
                            await UpdateStatusAsync(db, record.Id, FileProcessingStatus.Processing, null, stoppingToken);

                            // Reconstruct path maps from DB parameters
                            string baseFolder = record.Type == FileType.Image ? _env.WebRootPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "PrivateStorage");
                            string fullPath = Path.Combine(baseFolder, record.FolderName, record.FileName);

                            try
                            {
                                if (record.Type == FileType.Image)
                                {
                                    await fileService.RunImageOptimizationAsync(record.Id, fullPath, stoppingToken);
                                }
                                else
                                {
                                    string chunkFolder = Path.Combine(baseFolder, "VideoOutputChunks", Path.GetFileNameWithoutExtension(record.FileName));
                                    await fileService.RunVideoChunkingAsync(record.Id, fullPath, chunkFolder, stoppingToken);
                                }

                                await UpdateStatusAsync(db, record.Id, FileProcessingStatus.Completed, null, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Poller recovery execution failed for File ID: {Id}", record.Id);
                                await UpdateStatusAsync(db, record.Id, FileProcessingStatus.Failed, ex.Message, stoppingToken);
                            }
                        }
                    }
                }
                catch (Exception pollEx)
                {
                    _logger.LogError(pollEx, "Poller framework loop ran into an error condition.");
                }
            }
        }

        // Clean internal state tracking manager (Fixed parameter Guid id to long id alignment)
        private async Task UpdateStatusAsync(AppDbContext db, long id, FileProcessingStatus status, string? error, CancellationToken cancellationToken)
        {
            var match = await db.PendingFiles.FindAsync(new object?[] { id }, cancellationToken);
            if (match != null)
            {
                match.Status = status;
                match.ErrorMessage = error;
                if (status == FileProcessingStatus.Completed || status == FileProcessingStatus.Failed)
                    match.FinishedAt = DateTime.UtcNow;

                await db.SaveChangesAsync(cancellationToken);
            }
        }

    }
}
