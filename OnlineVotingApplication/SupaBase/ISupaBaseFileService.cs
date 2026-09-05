namespace OnlineVotingApplication.SupaBase
{
    public interface ISupaBaseFileService
    {
        Task<string> UploadFileAsync(string bucketName, string fileName, Stream fileStream, string contentType);
        Task<bool> DeleteFileAsync(string fileUrlOrPath, string bucketName);
    }
}
