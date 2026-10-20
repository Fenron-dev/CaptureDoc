using System.Collections.Concurrent;
using CaptureDoc.Core.Serialization;
using SkiaSharp;

namespace CaptureDoc.Rendering;

/// <summary>Decoded images for asset ids (decoding a 4K PNG takes ~50 ms, so cache it).</summary>
public sealed class AssetImageCache : IDisposable
{
    private readonly AssetStore _store;
    private readonly ConcurrentDictionary<string, SKImage> _images = new(StringComparer.OrdinalIgnoreCase);

    public AssetImageCache(AssetStore store) => _store = store;

    public AssetStore Store => _store;

    /// <summary>Registers an already decoded image (avoids re-decoding a fresh capture).</summary>
    public void Put(string id, SKImage image) => _images[id] = image;

    public SKImage? Get(string id)
    {
        if (_images.TryGetValue(id, out var img)) return img;
        var bytes = _store.Get(id);
        if (bytes is null) return null;
        var decoded = SKImage.FromEncodedData(bytes);
        if (decoded is null) return null;
        // Raster copy: GPU-independent and safe to share across threads
        var raster = decoded.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(raster, decoded)) decoded.Dispose();
        return _images.GetOrAdd(id, raster);
    }

    public void Dispose()
    {
        foreach (var i in _images.Values) i.Dispose();
        _images.Clear();
    }
}
