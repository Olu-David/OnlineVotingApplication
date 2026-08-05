using System;
using System.Threading;

namespace OnlineVotingApplication.Jobs
{
    // The core blueprint all background channel jobs must follow
    public interface IFileJob
    {
        long FileId { get; }
        string FilePath { get; }
        CancellationToken CancellationToken { get; }
    }

    // High-speed structure for processing image files
    public record ProcessImageJob(
        long FileId,
        string FilePath,
        CancellationToken CancellationToken = default) : IFileJob;

    // High-speed structure for breaking down video files
    public record ChunkVideoJob(
        long FileId,
        string FilePath,
        string OutPutFolder,
        CancellationToken CancellationToken = default) : IFileJob;
}
