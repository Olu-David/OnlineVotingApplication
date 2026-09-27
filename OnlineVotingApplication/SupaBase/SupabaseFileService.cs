using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.SupaBase;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Services
{
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
            var supabaseUrl = (_configuration["Supabase:Url"] ?? _configuration["SupabaseUrl"] ?? "").TrimEnd('/');

            if (string.IsNullOrEmpty(supabaseUrl))
            {
                var serviceUrl = _configuration["AWS:ServiceUrl"] ?? _configuration["AWS__ServiceUrl"] ?? "";
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
                var uri = new Uri(fileUrlOrPath, UriKind.RelativeOrAbsolute);
                string filePath = uri.IsAbsoluteUri ? uri.Segments.Last() : fileUrlOrPath;
                filePath = filePath.TrimStart('/');

                var deleteRequest = new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = filePath
                };

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
}