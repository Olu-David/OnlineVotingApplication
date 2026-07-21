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

        public async Task RegisterAndQueueUploadAsync(IFormFile file, FileType fileType, CancellationToken cancellationToken = default)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentNullException(nameof(file), "Uploaded file stream is empty or null.");
         

            var secureFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var targetPath = fileType == FileType.Image
                ? Path.Combine(_env.WebRootPath, "Optimized_Images", secureFileName)
                : Path.Combine(_privateStorageRoot, secureFileName);

            try
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

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

            // Create tracking row in your database table
            var pendingFile = new PendingFile
            {
                FileName = secureFileName,
                FolderName = fileType == FileType.Image ? "Optimized_Images" : "Private_Storage",
                Status = FileProcessingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _db.PendingFiles.Add(pendingFile);
            await _db.SaveChangesAsync(cancellationToken);

            // Dispatch using your exact record definitions and the full targetPath strings
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
        }

        public async Task RunImageOptimizationAsync(long fileId, string filePath, CancellationToken cancellationToken = default)
        {
            using (Image image = await Image.LoadAsync(filePath, cancellationToken))
            {
                image.Mutate(x => x.Resize(1200, 0)); // Maintain aspect ratio with 0
                await image.SaveAsync(filePath, cancellationToken);
            }
        }

        public async Task RunVideoChunkingAsync(long fileId, string filePath, string outputFolder, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            //We Need the Video upload to be broken down into bytes of 5mb. So we create an Interger that reeads 5mb
            const int FiveMegaByte = 5 * 1024 * 1024;
            //Converts it to byte
            byte[] streamingBuffer = new byte[FiveMegaByte];
            //We then Read the uploaaded file into that 5mb.

            using (var srcstream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                int step = 0;
                int readlength;
                //This is the actual codes that Run those Videos intoo Chunkss. so it reads the video by cut them into fragments of 5mb
                while ((readlength = await srcstream.ReadAsync(streamingBuffer, 0, streamingBuffer.Length, cancellationToken)) > 0)
                {

                    cancellationToken.ThrowIfCancellationRequested();
                    //We need a Path Where the Chunked Vidoes are Deposited into. So we create a fodder
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

        public bool DeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }
    }
}




















//      public async Task<PendingFile> SaveFileAsync(IFormFile file, string folderName, FileType type, CancellationToken cancellationToken)
//        {
//            if (file == null) throw new ArgumentNullException(nameof(file));
//            if (string.IsNullOrWhiteSpace(folderName)) throw new ArgumentNullException(nameof(folderName));
//            if (string.IsNullOrWhiteSpace(_env.WebRootPath)) throw new InvalidOperationException("WebRootPath configuration missing.");

//            //We have too look for a file or folder
//            var uploadFolder = Path.Combine(_env.WebRootPath, folderName);
//            //if the file or folder doesnt exist 
//            if (!Directory.Exists(uploadFolder))
//                //We Create a new Folder
//                Directory.CreateDirectory(uploadFolder);
//            //After Creating a folder, we give it a unique file name
//            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
//            //every File should have a path, in which can be found later
//            var fullPhysicalPath = Path.Combine(uploadFolder, uniqueFileName);

//            using (var stream = new FileStream(fullPhysicalPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
//            {
//                try
//                {
//                    await file.CopyToAsync(stream, cancellationToken);
//                }
//                catch (OperationCanceledException)
//                {
//                    if (File.Exists(fullPhysicalPath)) File.Delete(fullPhysicalPath);
//                    throw;
//                }
//            }

//            return new PendingFile
//            {
//                Id = Guid.NewGuid(),
//                FileName = uniqueFileName,
//                FolderName = folderName,
//                Type = type,
//                Status = FileProcessingStatus.Pending
//            };
//        }

//        public async Task<PendingFile?> ProcessChunkAsync(IFormFile chunk, string uniqueFileId, int chunkIndex, int totalChunks, string folderName, FileType type)
//        {
//            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
//            if (string.IsNullOrWhiteSpace(_env.WebRootPath)) throw new InvalidOperationException("WebRootPath missing.");

//            var targetFolder = Path.Combine(_env.WebRootPath, folderName);
//            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

//            var tempFileName = $"{uniqueFileId}.tmp";
//            var fullPhysicalPath = Path.Combine(targetFolder, tempFileName);

//            using (var stream = new FileStream(fullPhysicalPath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, useAsync: true))
//            {
//                await chunk.CopyToAsync(stream);
//            }

//            if (chunkIndex == totalChunks - 1)
//            {
//                return new PendingFile
//                {
//                    Id = Guid.Parse(uniqueFileId),
//                    FileName = tempFileName,
//                    FolderName = folderName,
//                    Type = type,
//                    Status = FileProcessingStatus.Pending
//                };
//            }

//            return null;
//        }

//        // =========================================================================
//        // 🚀 EXPLICIT PARSING SERVICES (Called by the Background Worker)
//        // =========================================================================

//        public async Task ParseVoterManifestAsync(string path, AppDbContext db, CancellationToken ct)
//        {
//            using var reader = new StreamReader(path);
//            int lineCounter = 0;
//            while (!reader.EndOfStream)
//            {
//                ct.ThrowIfCancellationRequested();
//                var line = await reader.ReadLineAsync(ct);
//                if (string.IsNullOrWhiteSpace(line)) continue;

//                var values = line.Split(',');
//                // db.Voters.Add(new Voter { FullName = values[0] });

//                lineCounter++;
//                if (lineCounter % 1000 == 0)
//                {
//                    await db.SaveChangesAsync(ct);
//                    db.ChangeTracker.Clear();
//                }
//            }
//            await db.SaveChangesAsync(ct);
//            db.ChangeTracker.Clear();
//        }

//        public async Task ParseCandidateListAsync(string path, AppDbContext db, CancellationToken ct)
//        {
//            using var reader = new StreamReader(path);
//            int lineCounter = 0;
//            while (!reader.EndOfStream)
//            {
//                ct.ThrowIfCancellationRequested();
//                var line = await reader.ReadLineAsync(ct);
//                if (string.IsNullOrWhiteSpace(line)) continue;

//                var values = line.Split(',');
//                // db.Candidates.Add(new Candidate { Name = values[0] });

//                lineCounter++;
//                if (lineCounter % 1000 == 0)
//                {
//                    await db.SaveChangesAsync(ct);
//                    db.ChangeTracker.Clear();
//                }
//            }
//            await db.SaveChangesAsync(ct);
//            db.ChangeTracker.Clear();
//        }

//        public async Task ParsePollingStationListAsync(string path, AppDbContext db, CancellationToken ct)
//        {
//            using var reader = new StreamReader(path);
//            int lineCounter = 0;
//            while (!reader.EndOfStream)
//            {
//                ct.ThrowIfCancellationRequested();
//                var line = await reader.ReadLineAsync(ct);
//                if (string.IsNullOrWhiteSpace(line)) continue;

//                var values = line.Split(',');
//                // db.PollingStations.Add(new PollingStation { StationName = values[0] });

//                lineCounter++;
//                if (lineCounter % 1000 == 0)
//                {
//                    await db.SaveChangesAsync(ct);
//                    db.ChangeTracker.Clear();
//                }
//            }
//            await db.SaveChangesAsync(ct);
//            db.ChangeTracker.Clear();
//        }
//    }
//}