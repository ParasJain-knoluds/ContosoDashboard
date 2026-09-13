# Contracts: IFileStorageService and IVirusScanner

This is a Blazor Server app with no external API consumers; the "contracts" for this feature are
the internal service interfaces that other components (pages, `DocumentService`) depend on, plus
the authorized download endpoint's request/response shape.

## `IFileStorageService`

```csharp
namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    /// Saves the stream to a system-generated path and returns the stored relative path.
    Task<string> UploadAsync(Stream fileStream, int userId, int? projectId, string fileExtension);

    Task DeleteAsync(string filePath);

    Task<Stream> DownloadAsync(string filePath);

    /// Returns a path/URL usable by the download endpoint; for local storage this may just
    /// echo back filePath, but the signature matches the future Azure Blob SAS-URL implementation.
    Task<string> GetUrlAsync(string filePath, TimeSpan expiration);
}
```

**Contract expectations**:
- `UploadAsync` MUST generate the storage path (`{userId}/{projectId|"personal"}/{guid}.{ext}`)
  internally and MUST NOT accept or use a caller-supplied filename in the path.
- `UploadAsync` MUST complete the physical write before returning; callers persist the returned path
  to the `Document.FilePath` column only after this call succeeds (orphan-avoidance ordering).
- `DownloadAsync` MUST throw a typed not-found exception (or return null, per implementation
  decision) if the path does not exist, so callers can return a 404 rather than crash.
- `DeleteAsync` MUST be idempotent (deleting a non-existent path is not an error).

## `IVirusScanner`

```csharp
namespace ContosoDashboard.Services;

public interface IVirusScanner
{
    /// Returns true if the file is considered clean; false if flagged as malicious.
    Task<bool> ScanAsync(Stream fileStream);
}
```

**Contract expectations**:
- `StubVirusScanner.ScanAsync` (training implementation) always returns `true` and MUST NOT make any
  network calls; a doc-comment MUST state this is a placeholder for a real AV engine.
- `ScanAsync` is invoked by `VirusScanBackgroundService` (not inline during the upload request) after
  the document is already saved with `ScanStatus = Scanning`; it is no longer on the upload request's
  critical path.

## `IScanQueue` and `VirusScanBackgroundService` (async scan pipeline)

```csharp
namespace ContosoDashboard.Services;

public interface IScanQueue
{
    ValueTask EnqueueAsync(int documentId, CancellationToken cancellationToken = default);
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
```

**Contract expectations**:
- `ScanQueue` (the local implementation) wraps a bounded `System.Threading.Channels.Channel<int>`.
  This is the offline stand-in for a real Azure Queue Storage queue; a production implementation
  could publish to Azure Queue Storage instead of an in-memory channel with no changes to callers.
- `DocumentService.UploadAsync` MUST call `IScanQueue.EnqueueAsync(documentId)` immediately after the
  `Document` row is committed with `ScanStatus = Scanning`, then return to the caller without waiting
  for the scan to complete.
- `VirusScanBackgroundService : BackgroundService` (registered via `AddHostedService`) loops calling
  `DequeueAsync`, resolves the `Document`, calls `IVirusScanner.ScanAsync` against the stored file
  (via `IFileStorageService.DownloadAsync`), and:
  - on clean result: sets `ScanStatus = Available`, logs a `DocumentActivityLog` entry;
  - on infected result: calls `IFileStorageService.DeleteAsync`, deletes the `Document` row, and
    triggers an in-app notification to the uploader explaining the rejection.
  This worker is the offline stand-in for a production Azure Functions Queue Storage trigger; the
  queue/scanner interfaces are the intended swap points, not the worker's internal loop structure.
- Any document with `ScanStatus != Available` MUST be excluded from all list/search/project-view
  results and download/preview/share operations for every user except the uploader (who may see its
  status but not download it while `Scanning`).

## Document download/preview endpoint (Razor Page handler)

`GET /documents/{documentId}/download` and `GET /documents/{documentId}/preview`

| Aspect | Behavior |
|---|---|
| Auth | Requires authenticated user (`[Authorize]` on the page/handler) |
| Authorization | `DocumentService` verifies the caller is the uploader, a member of the document's project, a recipient of a `DocumentShare`, a Team Lead/PM with project-level rights, or an Administrator — else returns 403. Also verifies `ScanStatus == Available` for any non-uploader caller; the uploader alone may see status metadata while `Scanning` but not download the bytes. |
| Not found | Returns 404 if `documentId` does not exist (no information leak about existence to unauthorized callers — treat unauthorized and not-found the same externally where feasible) |
| Success (download) | Streams file bytes with `Content-Disposition: attachment; filename="{OriginalFileName}"` |
| Success (preview) | Streams file bytes with `Content-Disposition: inline`, only for PDF/JPEG/PNG `FileType`s; other types redirect to download |
| Side effect | Logs a `DocumentActivityLog` row with `ActionType = Download` on successful streaming |

## `DocumentService` key operations (internal contract, consumed by Pages)

```csharp
Task<Document> UploadAsync(UploadDocumentRequest request, int currentUserId);
Task<IReadOnlyList<Document>> GetMyDocumentsAsync(int currentUserId, DocumentListFilter filter);
Task<IReadOnlyList<Document>> GetProjectDocumentsAsync(int projectId, int currentUserId);
Task<IReadOnlyList<Document>> SearchAsync(string query, int currentUserId);
Task<Document> UpdateMetadataAsync(int documentId, UpdateDocumentMetadataRequest request, int currentUserId);
Task ReplaceFileAsync(int documentId, Stream newFileStream, string fileExtension, string contentType, int currentUserId);
Task DeleteAsync(int documentId, int currentUserId);
Task ShareAsync(int documentId, ShareDocumentRequest request, int currentUserId);
```

Every method MUST perform its own authorization check (never trust that the UI only shows
permitted actions) per constitution Principle II / FR-028.
