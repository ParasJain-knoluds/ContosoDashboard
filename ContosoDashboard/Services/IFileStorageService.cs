namespace ContosoDashboard.Services;

/// Abstraction over document file storage; swap the implementation to target Azure Blob Storage later.
public interface IFileStorageService
{
    /// Saves the stream to a system-generated path and returns the stored relative path.
    Task<string> UploadAsync(Stream fileStream, int userId, int? projectId, string fileExtension);

    Task DeleteAsync(string filePath);

    Task<Stream> DownloadAsync(string filePath);

    /// Returns a path/URL usable by the download endpoint; local storage echoes filePath.
    Task<string> GetUrlAsync(string filePath, TimeSpan expiration);
}
