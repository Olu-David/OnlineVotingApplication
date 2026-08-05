using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Channels;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Jobs;

namespace OnlineVotingApplication.Repository.Services
{
    public class FileService : iFileService
    {
        private readonly IWebHostEnvironment _env;
        private readonly AppDbContext _db;
        private readonly FileChannel _channel;
        private readonly string _privateStorageRoot;

        public FileService(IWebHostEnvironment env, AppDbContext db, FileChannel channel)
        {
            _env = env;
            _db = db;
            _channel = channel;
            _privateStorageRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "PrivateStorage");
            if (!Directory.Exists(_privateStorageRoot)) Directory.CreateDirectory(_privateStorageRoot);
        }

        public async Task<string> RegisterAndQueueUploadAsync(
            IFormFile file,
            FileType fileType,
            string uploadFolder,
            CancellationToken cancellationToken = default)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentNullException(nameof(file), "Uploaded file stream is empty or null.");

            if (string.IsNullOrWhiteSpace(uploadFolder))
                throw new ArgumentException("Upload folder name cannot be null or empty.", nameof(uploadFolder));

            string baseDirectory = fileType == FileType.Image
                ? Path.Combine(_env.WebRootPath, uploadFolder)
                : Path.Combine(_privateStorageRoot, uploadFolder);

            if (!Directory.Exists(baseDirectory))
            {
                Directory.CreateDirectory(baseDirectory);
            }

            var secureFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var targetPath = Path.Combine(baseDirectory, secureFileName);

            // Physically persist file chunks to server storage instantly
            try
            {
                // Explicitly wrapping in a using block ensures the file lock is released instantly when writing finishes
                using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }
            }
            catch
            {
                if (File.Exists(targetPath)) File.Delete(targetPath);
                throw;
            }

            // Write safe tracking recovery row into DB logs
            var pendingFile = new PendingFile
            {
                FileName = secureFileName,
                FolderName = uploadFolder,
                Status = FileProcessingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _db.PendingFiles.Add(pendingFile);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception dbEx)
            {
                // CRUCIAL RECOVERY LAYER: If saving the record to your DB fails, clean up the disk file safely first
                DeleteFile(targetPath);
                throw new Exception($"File service failed to register file row in database. Inner: {dbEx.Message}", dbEx);
            }

            // Pass safe system file metrics directly down the pipeline channels
            if (fileType == FileType.Image)
            {
                var imageJob = new ProcessImageJob(pendingFile.Id, targetPath, cancellationToken);
                await _channel.Writer.WriteAsync(imageJob);
            }
            else
            {
                var chunkFolder = Path.Combine(_privateStorageRoot, "VideoOutputChunks", Path.GetFileNameWithoutExtension(secureFileName));
                var videoJob = new ChunkVideoJob(pendingFile.Id, targetPath, chunkFolder, cancellationToken);
                await _channel.Writer.WriteAsync(videoJob);
            }

            return secureFileName;
        }

        public async Task RunImageOptimizationAsync(long fileId, string filePath, CancellationToken cancellationToken = default)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".jpeg" || ext == ".jpg" || ext == ".png" || ext == ".webp")
            {
                using (Image image = await Image.LoadAsync(filePath, cancellationToken))
                {
                    image.Mutate(x => x.Resize(1200, 0));
                    await image.SaveAsync(filePath, cancellationToken);
                }
            }
        }

        public async Task RunVideoChunkingAsync(long fileId, string filePath, string outputFolder, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

            const int FiveMegaByte = 5 * 1024 * 1024;
            byte[] streamingBuffer = new byte[FiveMegaByte];

            using (var srcstream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                int step = 0;
                int readlength;
                while ((readlength = await srcstream.ReadAsync(streamingBuffer, 0, streamingBuffer.Length, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string segmentPath = Path.Combine(outputFolder, $"Fragment_{step:D4}.dat");
                    using (var targetChunk = new FileStream(segmentPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                    {
                        await targetChunk.WriteAsync(streamingBuffer, 0, readlength, cancellationToken);
                    }
                    step++;
                    await Task.Yield();
                }
            }

            if (File.Exists(filePath)) File.Delete(filePath);
        }

        // 🛠️ FIX IMPLEMENTATION: Added defensive polling loop to handle active stream blocks
        public bool DeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!File.Exists(path)) return false;

            int maxRetries = 5;
            int delayMs = 200;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    File.Delete(path);
                    return true;
                }
                catch (IOException ex)
                {
                    // Check exception runtime signatures for a file sharing violation lock code (32 / 33)
                    int hrCode = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF;
                    if (hrCode == 32 || hrCode == 33)
                    {
                        // Sleep thread briefly to give concurrent memory processes room to complete and unlock
                        Thread.Sleep(delayMs);
                        continue;
                    }
                    throw;
                }
            }
            return false;
        }
    }
}
