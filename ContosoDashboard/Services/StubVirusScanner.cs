namespace ContosoDashboard.Services;

/// Training placeholder for a real antivirus engine; always reports files clean, makes no network calls.
public class StubVirusScanner : IVirusScanner
{
    public Task<bool> ScanAsync(Stream fileStream)
    {
        return Task.FromResult(true);
    }
}
