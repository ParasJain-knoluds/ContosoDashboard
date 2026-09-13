using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

/// Hosted worker; training stand-in for a future Azure Functions Queue Storage trigger.
public class VirusScanBackgroundService : BackgroundService
{
    private readonly IScanQueue _queue;
    private readonly IVirusScanner _scanner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VirusScanBackgroundService> _logger;

    public VirusScanBackgroundService(
        IScanQueue queue,
        IVirusScanner scanner,
        IServiceScopeFactory scopeFactory,
        ILogger<VirusScanBackgroundService> logger)
    {
        _queue = queue;
        _scanner = scanner;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int documentId;
            try
            {
                documentId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessDocumentAsync(documentId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process virus scan for document {DocumentId}", documentId);
            }
        }
    }

    private async Task ProcessDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var document = await context.Documents.FindAsync(new object?[] { documentId }, cancellationToken);
        if (document == null)
        {
            return;
        }

        bool isClean;
        await using (var fileStream = await storage.DownloadAsync(document.FilePath))
        {
            isClean = await _scanner.ScanAsync(fileStream);
        }

        if (isClean)
        {
            document.ScanStatus = DocumentScanStatuses.Available;
            await context.SaveChangesAsync(cancellationToken);

            if (document.ProjectId.HasValue)
            {
                var memberIds = await context.ProjectMembers
                    .Where(pm => pm.ProjectId == document.ProjectId.Value && pm.UserId != document.UploadedByUserId)
                    .Select(pm => pm.UserId)
                    .ToListAsync(cancellationToken);

                foreach (var memberId in memberIds)
                {
                    await notificationService.CreateNotificationAsync(new Notification
                    {
                        UserId = memberId,
                        Title = "New document available",
                        Message = $"A new document \"{document.Title}\" was added to your project.",
                        Type = NotificationType.ProjectUpdate,
                        Priority = NotificationPriority.Informational
                    });
                }
            }
        }
        else
        {
            var uploaderId = document.UploadedByUserId;
            var title = document.Title;

            await storage.DeleteAsync(document.FilePath);
            context.Documents.Remove(document);
            await context.SaveChangesAsync(cancellationToken);

            await notificationService.CreateNotificationAsync(new Notification
            {
                UserId = uploaderId,
                Title = "Document upload rejected",
                Message = $"Your document \"{title}\" failed the security scan and was removed.",
                Type = NotificationType.SystemAnnouncement,
                Priority = NotificationPriority.Important
            });
        }
    }
}
