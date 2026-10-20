using System.Text.Json;
using System.Text.Json.Serialization;
using CaptureDoc.Core.Model;

namespace CaptureDoc.Core.Serialization;

public static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // NaN/Infinity would make files unreadable for other tools
            NumberHandling = JsonNumberHandling.Strict,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static T Clone<T>(T value) where T : class
    {
        var json = JsonSerializer.Serialize<object>(value, Compact);
        return (T)JsonSerializer.Deserialize(json, value.GetType(), Compact)!;
    }

    public static string SerializePage(Page page) => JsonSerializer.Serialize(page, Compact);
    public static Page DeserializePage(string json) => JsonSerializer.Deserialize<Page>(json, Compact)!;

    /// <summary>Copies the state of <paramref name="source"/> into <paramref name="target"/> (keeps the object identity).</summary>
    public static void RestorePageInto(Page target, string json)
    {
        var p = DeserializePage(json);
        target.Id = p.Id; target.Name = p.Name; target.Width = p.Width; target.Height = p.Height;
        target.Background = p.Background; target.Layers = p.Layers;
    }
}
