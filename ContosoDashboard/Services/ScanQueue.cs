using System.Threading.Channels;

namespace ContosoDashboard.Services;

/// Channel-backed queue; training stand-in for a future Azure Queue Storage queue.
public class ScanQueue : IScanQueue
{
    private readonly Channel<int> _channel = Channel.CreateBounded<int>(500);

    public ValueTask EnqueueAsync(int documentId, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(documentId, cancellationToken);
    }

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
