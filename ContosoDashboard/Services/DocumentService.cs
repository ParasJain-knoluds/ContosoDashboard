using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public enum DocumentSortField
{
    Title,
    UploadDate,
    Category,
    FileSize
}

public class DocumentListFilter
{
    public string? Category { get; set; }
    public int? ProjectId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public DocumentSortField SortBy { get; set; } = DocumentSortField.UploadDate;
    public bool SortDescending { get; set; } = true;
}

public class UploadDocumentRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
    public List<string>? Tags { get; set; }
    public Stream FileStream { get; set; } = Stream.Null;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

public class UpdateDocumentMetadataRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string>? Tags { get; set; }
}

public class ShareDocumentRequest
{
    public int? SharedWithUserId { get; set; }
    public int? SharedWithProjectId { get; set; }
}

public interface IDocumentService
{
    Task<Document> UploadAsync(UploadDocumentRequest request, int currentUserId);
    Task<List<Document>> GetMyDocumentsAsync(int currentUserId, DocumentListFilter? filter = null);
    Task<List<Document>> GetProjectDocumentsAsync(int projectId, int currentUserId);
    Task<List<Document>> SearchAsync(string query, int currentUserId);
    Task<List<Document>> GetSharedWithMeAsync(int currentUserId);
    Task<int> GetDocumentCountAsync(int currentUserId);
    Task<(Document Document, Stream Content)?> DownloadAsync(int documentId, int currentUserId);
    Task<Document?> UpdateMetadataAsync(int documentId, UpdateDocumentMetadataRequest request, int currentUserId);
    Task<bool> ReplaceFileAsync(int documentId, Stream newFileStream, string originalFileName, string contentType, long fileSizeBytes, int currentUserId);
    Task<bool> DeleteAsync(int documentId, int currentUserId);
    Task<bool> ShareAsync(int documentId, ShareDocumentRequest request, int currentUserId);
    Task<bool> AttachToProjectAsync(int documentId, int projectId, int currentUserId);
    Task<List<(string DocumentType, int Count)>> GetMostUploadedDocumentTypesAsync(int requestingUserId);
    Task<List<(string UploaderName, int Count)>> GetMostActiveUploadersAsync(int requestingUserId);
    Task<List<DocumentActivityLog>> GetDocumentAccessPatternsAsync(int requestingUserId);
}

public class DocumentService : IDocumentService
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    private static readonly HashSet<string> PreviewableFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/png"
    };

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IScanQueue _scanQueue;
    private readonly INotificationService _notificationService;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorage,
        IScanQueue scanQueue,
        INotificationService notificationService)
    {
        _context = context;
        _fileStorage = fileStorage;
        _scanQueue = scanQueue;
        _notificationService = notificationService;
    }

    // ---------------------------------------------------------------------
    // Authorization helpers
    // ---------------------------------------------------------------------

    private async Task<bool> IsAdministratorAsync(int userId)
    {
        var role = await _context.Users
            .Where(u => u.UserId == userId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync();

        return role == UserRole.Administrator;
    }

    private async Task<bool> IsProjectManagerForAsync(int projectId, int userId)
    {
        return await _context.Projects.AnyAsync(p => p.ProjectId == projectId && p.ProjectManagerId == userId);
    }

    private async Task<bool> IsProjectMemberAsync(int projectId, int userId)
    {
        return await _context.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }

    /// Accessible = uploaded by the caller (any status), or Available and (project member/manager, admin, or shared with the caller/their project).
    private IQueryable<Document> BuildAccessibleDocumentsQuery(int userId, bool isAdmin)
    {
        if (isAdmin)
        {
            return _context.Documents.AsQueryable();
        }

        var memberProjectIds = _context.ProjectMembers.Where(pm => pm.UserId == userId).Select(pm => pm.ProjectId);
        var managedProjectIds = _context.Projects.Where(p => p.ProjectManagerId == userId).Select(p => p.ProjectId);
        var sharedDocumentIds = _context.DocumentShares
            .Where(s => s.SharedWithUserId == userId ||
                        (s.SharedWithProjectId != null && memberProjectIds.Contains(s.SharedWithProjectId.Value)))
            .Select(s => s.DocumentId);

        return _context.Documents.Where(d =>
            d.UploadedByUserId == userId ||
            (d.ScanStatus == DocumentScanStatuses.Available && (
                (d.ProjectId != null && (memberProjectIds.Contains(d.ProjectId.Value) || managedProjectIds.Contains(d.ProjectId.Value))) ||
                sharedDocumentIds.Contains(d.DocumentId)
            )));
    }

    // ---------------------------------------------------------------------
    // Validation
    // ---------------------------------------------------------------------

    private static void ValidateUpload(UploadDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Title is required.");
        }

        if (!DocumentCategories.All.Contains(request.Category))
        {
            throw new ArgumentException("Category is not one of the supported values.");
        }

        if (request.FileSizeBytes > MaxFileSizeBytes)
        {
            throw new ArgumentException("File exceeds the 25 MB size limit.");
        }

        var extension = Path.GetExtension(request.OriginalFileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.ContainsKey(extension))
        {
            throw new ArgumentException("File type is not supported.");
        }

        ValidateTags(request.Tags);
    }

    private static void ValidateTags(List<string>? tags)
    {
        if (tags == null)
        {
            return;
        }

        if (tags.Count > 10)
        {
            throw new ArgumentException("A document may have at most 10 tags.");
        }

        if (tags.Any(t => t.Length > 50))
        {
            throw new ArgumentException("Each tag may be at most 50 characters.");
        }
    }

    // ---------------------------------------------------------------------
    // Upload (User Story 1)
    // ---------------------------------------------------------------------

    public async Task<Document> UploadAsync(UploadDocumentRequest request, int currentUserId)
    {
        ValidateUpload(request);

        var extension = Path.GetExtension(request.OriginalFileName);
        var filePath = await _fileStorage.UploadAsync(request.FileStream, currentUserId, request.ProjectId, extension);

        var document = new Document
        {
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            Tags = request.Tags is { Count: > 0 } ? string.Join(",", request.Tags) : null,
            ProjectId = request.ProjectId,
            UploadedByUserId = currentUserId,
            UploadedDate = DateTime.UtcNow,
            FileSizeBytes = request.FileSizeBytes,
            FileType = request.ContentType,
            FilePath = filePath,
            OriginalFileName = request.OriginalFileName,
            ScanStatus = DocumentScanStatuses.Scanning,
            LastModifiedDate = DateTime.UtcNow
        };

        _context.Documents.Add(document);
        _context.DocumentActivityLogs.Add(new DocumentActivityLog
        {
            Document = document,
            UserId = currentUserId,
            ActionType = DocumentActivityActionTypes.Upload,
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await _scanQueue.EnqueueAsync(document.DocumentId);

        return document;
    }

    // ---------------------------------------------------------------------
    // Browse, search, access (User Story 2)
    // ---------------------------------------------------------------------

    public async Task<List<Document>> GetMyDocumentsAsync(int currentUserId, DocumentListFilter? filter = null)
    {
        filter ??= new DocumentListFilter();

        var query = _context.Documents
            .Include(d => d.Project)
            .Where(d => d.UploadedByUserId == currentUserId);

        query = ApplyFilter(query, filter);
        query = ApplySort(query, filter);

        return await query.ToListAsync();
    }

    public async Task<List<Document>> GetProjectDocumentsAsync(int projectId, int currentUserId)
    {
        var isAdmin = await IsAdministratorAsync(currentUserId);
        if (!isAdmin && !await IsProjectMemberAsync(projectId, currentUserId) && !await IsProjectManagerForAsync(projectId, currentUserId))
        {
            return new List<Document>();
        }

        return await _context.Documents
            .Include(d => d.UploadedByUser)
            .Where(d => d.ProjectId == projectId && (d.ScanStatus == DocumentScanStatuses.Available || d.UploadedByUserId == currentUserId))
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();
    }

    public async Task<List<Document>> SearchAsync(string query, int currentUserId)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<Document>();
        }

        var isAdmin = await IsAdministratorAsync(currentUserId);
        var accessible = BuildAccessibleDocumentsQuery(currentUserId, isAdmin)
            .Include(d => d.UploadedByUser)
            .Include(d => d.Project);

        var lowered = query.Trim();

        return await accessible
            .Where(d =>
                d.Title.Contains(lowered) ||
                (d.Description != null && d.Description.Contains(lowered)) ||
                (d.Tags != null && d.Tags.Contains(lowered)) ||
                d.UploadedByUser.DisplayName.Contains(lowered) ||
                (d.Project != null && d.Project.Name.Contains(lowered)))
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();
    }

    public async Task<List<Document>> GetSharedWithMeAsync(int currentUserId)
    {
        var memberProjectIds = _context.ProjectMembers.Where(pm => pm.UserId == currentUserId).Select(pm => pm.ProjectId);

        var sharedDocumentIds = await _context.DocumentShares
            .Where(s => s.SharedWithUserId == currentUserId ||
                        (s.SharedWithProjectId != null && memberProjectIds.Contains(s.SharedWithProjectId.Value)))
            .Select(s => s.DocumentId)
            .Distinct()
            .ToListAsync();

        return await _context.Documents
            .Include(d => d.UploadedByUser)
            .Where(d => sharedDocumentIds.Contains(d.DocumentId) && d.ScanStatus == DocumentScanStatuses.Available)
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();
    }

    public async Task<int> GetDocumentCountAsync(int currentUserId)
    {
        return await _context.Documents.CountAsync(d => d.UploadedByUserId == currentUserId);
    }

    public async Task<(Document Document, Stream Content)?> DownloadAsync(int documentId, int currentUserId)
    {
        var isAdmin = await IsAdministratorAsync(currentUserId);
        var document = await BuildAccessibleDocumentsQuery(currentUserId, isAdmin)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (document == null || document.ScanStatus != DocumentScanStatuses.Available)
        {
            return null;
        }

        var stream = await _fileStorage.DownloadAsync(document.FilePath);

        _context.DocumentActivityLogs.Add(new DocumentActivityLog
        {
            DocumentId = document.DocumentId,
            UserId = currentUserId,
            ActionType = DocumentActivityActionTypes.Download,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return (document, stream);
    }

    public static bool IsPreviewable(string fileType) => PreviewableFileTypes.Contains(fileType);

    private static IQueryable<Document> ApplyFilter(IQueryable<Document> query, DocumentListFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(d => d.Category == filter.Category);
        }

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(d => d.ProjectId == filter.ProjectId);
        }

        if (filter.DateFrom.HasValue)
        {
            query = query.Where(d => d.UploadedDate >= filter.DateFrom.Value);
        }

        if (filter.DateTo.HasValue)
        {
            query = query.Where(d => d.UploadedDate <= filter.DateTo.Value);
        }

        return query;
    }

    private static IQueryable<Document> ApplySort(IQueryable<Document> query, DocumentListFilter filter)
    {
        return filter.SortBy switch
        {
            DocumentSortField.Title => filter.SortDescending ? query.OrderByDescending(d => d.Title) : query.OrderBy(d => d.Title),
            DocumentSortField.Category => filter.SortDescending ? query.OrderByDescending(d => d.Category) : query.OrderBy(d => d.Category),
            DocumentSortField.FileSize => filter.SortDescending ? query.OrderByDescending(d => d.FileSizeBytes) : query.OrderBy(d => d.FileSizeBytes),
            _ => filter.SortDescending ? query.OrderByDescending(d => d.UploadedDate) : query.OrderBy(d => d.UploadedDate)
        };
    }

    // ---------------------------------------------------------------------
    // Manage and share (User Story 3)
    // ---------------------------------------------------------------------

    public async Task<Document?> UpdateMetadataAsync(int documentId, UpdateDocumentMetadataRequest request, int currentUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null || document.UploadedByUserId != currentUserId)
        {
            return null;
        }

        if (!DocumentCategories.All.Contains(request.Category))
        {
            throw new ArgumentException("Category is not one of the supported values.");
        }

        ValidateTags(request.Tags);

        document.Title = request.Title;
        document.Description = request.Description;
        document.Category = request.Category;
        document.Tags = request.Tags is { Count: > 0 } ? string.Join(",", request.Tags) : null;
        document.LastModifiedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return document;
    }

    public async Task<bool> ReplaceFileAsync(int documentId, Stream newFileStream, string originalFileName, string contentType, long fileSizeBytes, int currentUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null || document.UploadedByUserId != currentUserId)
        {
            return false;
        }

        if (fileSizeBytes > MaxFileSizeBytes)
        {
            throw new ArgumentException("File exceeds the 25 MB size limit.");
        }

        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.ContainsKey(extension))
        {
            throw new ArgumentException("File type is not supported.");
        }

        var oldFilePath = document.FilePath;
        var newFilePath = await _fileStorage.UploadAsync(newFileStream, currentUserId, document.ProjectId, extension);

        document.FilePath = newFilePath;
        document.FileSizeBytes = fileSizeBytes;
        document.FileType = contentType;
        document.OriginalFileName = originalFileName;
        document.LastModifiedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _fileStorage.DeleteAsync(oldFilePath);

        return true;
    }

    public async Task<bool> DeleteAsync(int documentId, int currentUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null)
        {
            return false;
        }

        var isUploader = document.UploadedByUserId == currentUserId;
        var isProjectManager = document.ProjectId.HasValue && await IsProjectManagerForAsync(document.ProjectId.Value, currentUserId);
        var isAdmin = await IsAdministratorAsync(currentUserId);

        if (!isUploader && !isProjectManager && !isAdmin)
        {
            return false;
        }

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();
        await _fileStorage.DeleteAsync(document.FilePath);

        return true;
    }

    public async Task<bool> ShareAsync(int documentId, ShareDocumentRequest request, int currentUserId)
    {
        var hasSingleTarget = (request.SharedWithUserId.HasValue) ^ (request.SharedWithProjectId.HasValue);
        if (!hasSingleTarget)
        {
            throw new ArgumentException("Exactly one of SharedWithUserId or SharedWithProjectId must be provided.");
        }

        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null || document.UploadedByUserId != currentUserId || document.ScanStatus != DocumentScanStatuses.Available)
        {
            return false;
        }

        var share = new DocumentShare
        {
            DocumentId = documentId,
            SharedByUserId = currentUserId,
            SharedWithUserId = request.SharedWithUserId,
            SharedWithProjectId = request.SharedWithProjectId,
            SharedDate = DateTime.UtcNow
        };

        _context.DocumentShares.Add(share);
        _context.DocumentActivityLogs.Add(new DocumentActivityLog
        {
            DocumentId = documentId,
            UserId = currentUserId,
            ActionType = DocumentActivityActionTypes.Share,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var recipientUserIds = new List<int>();
        if (request.SharedWithUserId.HasValue)
        {
            recipientUserIds.Add(request.SharedWithUserId.Value);
        }
        else if (request.SharedWithProjectId.HasValue)
        {
            recipientUserIds.AddRange(await _context.ProjectMembers
                .Where(pm => pm.ProjectId == request.SharedWithProjectId.Value && pm.UserId != currentUserId)
                .Select(pm => pm.UserId)
                .ToListAsync());
        }

        foreach (var recipientId in recipientUserIds)
        {
            await _notificationService.CreateNotificationAsync(new Notification
            {
                UserId = recipientId,
                Title = "A document was shared with you",
                Message = $"\"{document.Title}\" was shared with you.",
                Type = NotificationType.ProjectUpdate,
                Priority = NotificationPriority.Informational
            });
        }

        return true;
    }

    /// Attaches (re-parents) an existing document to a project, e.g. when linking a document to a task's project (FR-022).
    public async Task<bool> AttachToProjectAsync(int documentId, int projectId, int currentUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null || document.UploadedByUserId != currentUserId)
        {
            return false;
        }

        if (!await IsProjectMemberAsync(projectId, currentUserId) && !await IsProjectManagerForAsync(projectId, currentUserId))
        {
            return false;
        }

        if (document.ProjectId == projectId)
        {
            return true;
        }

        document.ProjectId = projectId;
        document.LastModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    // ---------------------------------------------------------------------
    // Admin reporting and audit (User Story 5)
    // ---------------------------------------------------------------------

    public async Task<List<(string DocumentType, int Count)>> GetMostUploadedDocumentTypesAsync(int requestingUserId)
    {
        if (!await IsAdministratorAsync(requestingUserId))
        {
            return new List<(string, int)>();
        }

        var results = await _context.Documents
            .GroupBy(d => d.FileType)
            .Select(g => new { FileType = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync();

        return results.Select(r => (r.FileType, r.Count)).ToList();
    }

    public async Task<List<(string UploaderName, int Count)>> GetMostActiveUploadersAsync(int requestingUserId)
    {
        if (!await IsAdministratorAsync(requestingUserId))
        {
            return new List<(string, int)>();
        }

        var results = await _context.Documents
            .Include(d => d.UploadedByUser)
            .GroupBy(d => d.UploadedByUser.DisplayName)
            .Select(g => new { UploaderName = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync();

        return results.Select(r => (r.UploaderName, r.Count)).ToList();
    }

    public async Task<List<DocumentActivityLog>> GetDocumentAccessPatternsAsync(int requestingUserId)
    {
        if (!await IsAdministratorAsync(requestingUserId))
        {
            return new List<DocumentActivityLog>();
        }

        return await _context.DocumentActivityLogs
            .Include(l => l.Document)
            .Include(l => l.User)
            .OrderByDescending(l => l.Timestamp)
            .Take(200)
            .ToListAsync();
    }
}
