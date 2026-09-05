using Supabase.Storage;
using OnlineVotingApplication.SupaBase;
using FileOptions = Supabase.Storage.FileOptions;

namespace OnlineVotingApplication.Services;

public class SupabaseFileService : ISupaBaseFileService
{
    private readonly Supabase.Client _supabaseClient;
    private readonly ILogger<SupabaseFileService> _logger;      

    public SupabaseFileService(Supabase.Client supabaseClient, ILogger<SupabaseFileService>logger)
    {
        _supabaseClient = supabaseClient;
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

        // 2. Convert Stream to Byte Array
        byte[] fileBytes;
        if (fileStream is MemoryStream ms)
        {
            fileBytes = ms.ToArray();
        }
        else
        {
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            fileBytes = memoryStream.ToArray();
        }

        // 3. Configure Upload Options
        var options = new FileOptions
        {
            ContentType = contentType,
            Upsert = true // Automatically overwrite if the file already exists
        };

        // 4. Upload File to Supabase Storage Bucket
        await _supabaseClient.Storage
            .From(bucketName)
            .Upload(fileBytes, fileName, options);

        // 5. Return Public Absolute URL
        return _supabaseClient.Storage
            .From(bucketName)
            .GetPublicUrl(fileName);
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

            // Call Supabase Storage API to remove the file
            var result = await _supabaseClient.Storage
                .From(bucketName)
                .Remove(new List<string> { filePath });

            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file from Supabase storage: {Path}", fileUrlOrPath);
            return false;
        }
    }
}