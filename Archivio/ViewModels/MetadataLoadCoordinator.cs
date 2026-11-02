using Archivio.Models;

namespace Archivio.ViewModels;

internal sealed class MetadataLoadCoordinator
{
    private const int DefaultMaxAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(150);

    private readonly SemaphoreSlim _readSemaphore;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    internal MetadataLoadCoordinator(SemaphoreSlim readSemaphore)
        : this(readSemaphore, DefaultMaxAttempts, DefaultRetryDelay)
    {
    }

    internal MetadataLoadCoordinator(SemaphoreSlim readSemaphore, int maxAttempts, TimeSpan retryDelay)
    {
        _readSemaphore = readSemaphore ?? throw new ArgumentNullException(nameof(readSemaphore));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay;
    }

    internal async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _readSemaphore.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _readSemaphore.Release();
        }
    }

    internal async Task<VideoMetadataLoadResult> LoadWithRetryAsync(
        Func<CancellationToken, Task<VideoMetadataLoadResult>> loadMetadataAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loadMetadataAsync);

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await loadMetadataAsync(cancellationToken);
            if (result.TechnicalPropertiesLoaded || result.WindowsPropertiesLoaded || attempt == _maxAttempts)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromTicks(_retryDelay.Ticks * attempt), cancellationToken);
        }

        throw new InvalidOperationException("Metadata loading completed without producing a result.");
    }
}
