using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CaptureDoc.Core.Serialization;

/// <summary>Content-addressed, immutable media blobs of an open project (id = SHA-256 hex).</summary>
public sealed class AssetStore
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.OrdinalIgnoreCase);

    public static string ComputeId(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public string Add(byte[] data)
    {
        var id = ComputeId(data);
        _blobs.TryAdd(id, data);
        return id;
    }

    internal void AddVerified(string id, byte[] data) => _blobs[id] = data;

    public bool Contains(string id) => _blobs.ContainsKey(id);
    public byte[]? Get(string id) => _blobs.TryGetValue(id, out var b) ? b : null;
    public IEnumerable<string> Ids => _blobs.Keys;
    public void Remove(string id) => _blobs.TryRemove(id, out _);
}
