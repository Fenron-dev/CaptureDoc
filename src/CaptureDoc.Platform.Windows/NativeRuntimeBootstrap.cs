using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CaptureDoc.Platform.Windows;

/// <summary>
/// Makes native libraries load from &lt;AppRoot&gt;/Runtime/win-x64 instead of being extracted to %TEMP%.
/// Must run before any SkiaSharp/Avalonia type initializes. See DevVault ADR-002.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NativeRuntimeBootstrap
{
    public static string? RuntimeDirectory { get; private set; }

    public static void Configure(string appRoot, params Assembly[] nativeConsumers)
    {
        var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var dir = Path.Combine(appRoot, "Runtime", rid);
        if (!Directory.Exists(dir)) return; // dev build: natives next to the exe
        RuntimeDirectory = dir;
        Native.SetDllDirectory(dir); // LoadLibrary-based loaders (ANGLE)
        foreach (var asm in nativeConsumers.Distinct())
        {
            try
            {
                NativeLibrary.SetDllImportResolver(asm, (name, _, _) =>
                {
                    var file = Path.Combine(dir, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll");
                    return File.Exists(file) && NativeLibrary.TryLoad(file, out var h) ? h : 0;
                });
            }
            catch (InvalidOperationException) { /* resolver already set */ }
        }
    }
}
