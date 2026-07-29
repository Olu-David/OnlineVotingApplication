using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iFileService
    {

        bool DeleteFile(string path);
        Task<string> RegisterAndQueueUploadAsync(IFormFile file, FileType fileType, string uploadFolder, CancellationToken cancellationToken = default);
        Task RunImageOptimizationAsync(long fileId, string filePath, CancellationToken cancellationToken = default);
        Task RunVideoChunkingAsync(long fileId, string filePath, string outputFolder, CancellationToken cancellationToken = default);

        
        }
   
}
