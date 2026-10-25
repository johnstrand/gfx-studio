using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>
/// Thread-safe LRU cache for decoded external <see cref="SKBitmap"/> assets across batch runs and animation frames.
/// </summary>
public sealed class GfxImageCache
{
    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _lru = new();
    private int _hitCount;
    private int _missCount;

    public GfxImageCache(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public static GfxImageCache Shared { get; } = new();

    public int HitCount => Volatile.Read(ref _hitCount);

    public int MissCount => Volatile.Read(ref _missCount);

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _map.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (var entry in _lru)
            {
                entry.Bitmap.Dispose();
            }

            _lru.Clear();
            _map.Clear();
            _hitCount = 0;
            _missCount = 0;
        }
    }

    public SKBitmap? GetOrLoad(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var normalizedPath = Path.GetFullPath(fullPath);
        if (!File.Exists(normalizedPath))
        {
            return null;
        }

        var lastWriteTicks = File.GetLastWriteTimeUtc(normalizedPath).Ticks;

        lock (_sync)
        {
            if (_map.TryGetValue(normalizedPath, out var existingNode))
            {
                if (existingNode.Value.LastWriteTicks == lastWriteTicks)
                {
                    _lru.Remove(existingNode);
                    _lru.AddFirst(existingNode);
                    _hitCount++;
                    return existingNode.Value.Bitmap.Copy();
                }

                existingNode.Value.Bitmap.Dispose();
                _lru.Remove(existingNode);
                _map.Remove(normalizedPath);
            }

            var decoded = DecodeBitmapFromFile(normalizedPath);
            if (decoded is null)
            {
                return null;
            }

            _missCount++;
            while (_map.Count >= _capacity && _lru.Last is { } oldest)
            {
                oldest.Value.Bitmap.Dispose();
                _map.Remove(oldest.Value.Path);
                _lru.RemoveLast();
            }

            var node = new LinkedListNode<CacheEntry>(new CacheEntry(normalizedPath, lastWriteTicks, decoded));
            _lru.AddFirst(node);
            _map[normalizedPath] = node;
            return decoded.Copy();
        }
    }

    private static SKBitmap? DecodeBitmapFromFile(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
        {
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private sealed record CacheEntry(string Path, long LastWriteTicks, SKBitmap Bitmap);
}
