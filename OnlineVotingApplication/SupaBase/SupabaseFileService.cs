using OnlineVotingApplication.SupaBase;
using Supabase;
using Supabase.Storage; // 🌟 Make sure this namespace is included

public class SupabaseFileService : ISupaBaseFileService
{
    private readonly Supabase.Client _supabaseClient;

    public SupabaseFileService(Supabase.Client supabaseClient)
    {
        _supabaseClient = supabaseClient;
    }

    public async Task<string> UploadFileAsync(string bucketName, string fileName, Stream fileStream, string contentType)
    {
        if (fileStream == null || fileStream.Length == 0)
        {
            throw new ArgumentException("File stream cannot be empty.");
        }

        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        byte[] fileBytes = memoryStream.ToArray();

        // 🌟 FIX: Use FileOptions instead of TransformOptions
        var options = new Supabase.Storage.FileOptions
        {
            ContentType = contentType,
            Upsert = true // Automatically overwrites if the file name already exists
        };

        // Target the bucket and pass the bytes along with options configuration
        await _supabaseClient.Storage
            .From(bucketName)
            .Upload(fileBytes, fileName, options);

        // Generate and return your absolute public link string
        return _supabaseClient.Storage
            .From(bucketName)
            .GetPublicUrl(fileName);
    }
}
