namespace ContosoDashboard.Services;

/// Local filesystem implementation, training stand-in for a future AzureBlobStorageService.
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;

    public LocalFileStorageService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["DocumentStorage:BasePath"] ?? "AppData/uploads";
        _basePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> UploadAsync(Stream fileStream, int userId, int? projectId, string fileExtension)
    {
        // Storage path is always system-generated; never derived from a user-supplied filename (path traversal protection)
        var relativePath = Path.Combine(
            userId.ToString(),
            projectId?.ToString() ?? "personal",
            $"{Guid.NewGuid()}{fileExtension}");

        var fullPath = Path.Combine(_basePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var destination = File.Create(fullPath))
        {
            await fileStream.CopyToAsync(destination);
        }

        return relativePath.Replace('\\', '/');
    }

    public Task DeleteAsync(string filePath)
    {
        var fullPath = Path.Combine(_basePath, filePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string filePath)
    {
        var fullPath = Path.Combine(_basePath, filePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored document file was not found.", filePath);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task<string> GetUrlAsync(string filePath, TimeSpan expiration)
    {
        // Local storage has no direct URL; callers must use the authorized download endpoint.
        return Task.FromResult(filePath);
    }
}
