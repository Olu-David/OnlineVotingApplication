using Amazon.S3;
using Amazon.S3.Model;
using OnlineVotingApplication.SupaBase;

namespace OnlineVotingApplication.Services;

public class SupabaseFileService : ISupaBaseFileService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SupabaseFileService> _logger;

    public SupabaseFileService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<SupabaseFileService> logger)
    {
        _s3Client = s3Client;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(string bucketName, string fileName, Stream fileStream, string contentType)
    {
        if (fileStream == null || fileStream.Length == 0)
        {
            throw new ArgumentException("File stream cannot be empty.", nameof(fileStream));
        }

        // 1. Reset stream position if seekable
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        // 2. Prepare the S3 Put Object Request
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = fileName,
            InputStream = fileStream,
            ContentType = contentType
        };

        // 3. Upload File to Supabase Storage via S3 Protocol
        await _s3Client.PutObjectAsync(putRequest);

        // 4. Construct and return the Public Absolute URL
        // Format: {SupabaseUrl}/storage/v1/object/public/{bucketName}/{fileName}
        var supabaseUrl = (_configuration["Supabase:Url"] ?? _configuration["SupabaseUrl"] ?? "").TrimEnd('/');

        if (string.IsNullOrEmpty(supabaseUrl))
        {
            // Fallback if extracting endpoint from AWS service URL
            var serviceUrl = _configuration["AWS:ServiceUrl"] ?? "";
            // e.g., https://xyz.supabase.co/storage/v1/s3 -> https://xyz.supabase.co
            if (serviceUrl.Contains("/storage/v1"))
            {
                supabaseUrl = serviceUrl.Substring(0, serviceUrl.IndexOf("/storage/v1"));
            }
        }

        return $"{supabaseUrl}/storage/v1/object/public/{bucketName}/{fileName}";
    }

    public async Task<bool> DeleteFileAsync(string fileUrlOrPath, string bucketName)
    {
        try
        {
            // Extract the relative file path if a full URL was passed
            var uri = new Uri(fileUrlOrPath, UriKind.RelativeOrAbsolute);
            string filePath = uri.IsAbsoluteUri ? uri.Segments.Last() : fileUrlOrPath;

            // Clean up leading slashes if necessary
            filePath = filePath.TrimStart('/');

            // Prepare S3 Delete Request
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = filePath
            };

            // Call S3 API to remove the file
            var response = await _s3Client.DeleteObjectAsync(deleteRequest);

            return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent ||
                   response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file from Supabase S3 storage: {Path}", fileUrlOrPath);
            return false;
        }
    }
}