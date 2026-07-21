using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iFileService
    {

        bool DeleteFile(string path);
        //Task<PendingFile> SaveFileAsync(IFormFile file, string folderName, FileType type, CancellationToken cancellationToken);
        Task RegisterAndQueueUploadAsync(IFormFile file, FileType fileType, CancellationToken cancellationToken = default);
        Task RunImageOptimizationAsync(long fileId, string filePath, CancellationToken cancellationToken = default);
        Task RunVideoChunkingAsync(long fileId, string filePath, string outputFolder, CancellationToken cancellationToken = default);

        
        }
   
}
