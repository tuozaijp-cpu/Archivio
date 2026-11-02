namespace Archivio.ViewModels;

internal sealed class ThumbnailCacheEntry
{
    public ulong FileSize { get; init; }
    public DateTimeOffset LastWriteTime { get; init; }
    public byte[] ImageData { get; init; } = Array.Empty<byte>();
    public bool LoadFailed { get; init; }
}

internal sealed class ThumbnailImageCache
{
    internal const long DefaultMaxImageDataBytes = 32L * 1024 * 1024;
    internal const int DefaultMaxEntries = 256;

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, CacheItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _leastRecentlyUsed = new();
    private readonly int _maxEntries;
    private readonly long _maxImageDataBytes;
    private long _imageDataBytes;

    private sealed class CacheItem
    {
        public ThumbnailCacheEntry Entry { get; init; } = new();
        public LinkedListNode<string> Node { get; init; } = null!;
    }

    public ThumbnailImageCache()
        : this(DefaultMaxEntries, DefaultMaxImageDataBytes)
    {
    }

    internal ThumbnailImageCache(int maxEntries, long maxImageDataBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxImageDataBytes);
        _maxEntries = maxEntries;
        _maxImageDataBytes = maxImageDataBytes;
    }

    internal int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _items.Count;
            }
        }
    }

    internal long ImageDataBytes
    {
        get
        {
            lock (_syncRoot)
            {
                return _imageDataBytes;
            }
        }
    }

    public bool TryGet(string path, out ThumbnailCacheEntry entry)
    {
        lock (_syncRoot)
        {
            if (!_items.TryGetValue(path, out var item))
            {
                entry = null!;
                return false;
            }

            _leastRecentlyUsed.Remove(item.Node);
            _leastRecentlyUsed.AddLast(item.Node);
            entry = item.Entry;
            return true;
        }
    }

    public void Set(string path, ThumbnailCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entry);

        lock (_syncRoot)
        {
            RemoveCore(path);
            if (entry.ImageData.LongLength > _maxImageDataBytes)
            {
                return;
            }

            var node = _leastRecentlyUsed.AddLast(path);
            _items[path] = new CacheItem { Entry = entry, Node = node };
            _imageDataBytes += entry.ImageData.LongLength;

            while (_items.Count > _maxEntries || _imageDataBytes > _maxImageDataBytes)
            {
                RemoveCore(_leastRecentlyUsed.First!.Value);
            }
        }
    }

    public void Remove(string path)
    {
        lock (_syncRoot)
        {
            RemoveCore(path);
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _items.Clear();
            _leastRecentlyUsed.Clear();
            _imageDataBytes = 0;
        }
    }

    public void Retain(IReadOnlyCollection<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var currentPaths = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        lock (_syncRoot)
        {
            foreach (var cachedPath in _items.Keys.Where(path => !currentPaths.Contains(path)).ToArray())
            {
                RemoveCore(cachedPath);
            }
        }
    }

    private void RemoveCore(string path)
    {
        if (!_items.Remove(path, out var item))
        {
            return;
        }

        _leastRecentlyUsed.Remove(item.Node);
        _imageDataBytes -= item.Entry.ImageData.LongLength;
    }
}
