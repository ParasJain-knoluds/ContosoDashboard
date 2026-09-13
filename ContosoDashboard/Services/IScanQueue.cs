namespace ContosoDashboard.Services;

/// In-process background job queue for document scan requests.
public interface IScanQueue
{
    ValueTask EnqueueAsync(int documentId, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
