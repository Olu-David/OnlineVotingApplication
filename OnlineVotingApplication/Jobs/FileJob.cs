using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Jobs
{
    public interface IFileJob { };
    public record ProcessImageJob(long FileId, string FilePath, CancellationToken CancellationToken) : IFileJob;
    public record ChunkVideoJob(long FileId, string FilePath, string OutPutFolder,  CancellationToken CancellationToken) : IFileJob;


}
