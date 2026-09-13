namespace ContosoDashboard.Services;

/// Abstraction over malware scanning of uploaded files.
public interface IVirusScanner
{
    /// Returns true if the file is considered clean; false if flagged as malicious.
    Task<bool> ScanAsync(Stream fileStream);
}
