using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using CaptureDoc.Core.Portable;

namespace CaptureDoc.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var appRoot = PortableEnvironment.FindAppRoot(AppContext.BaseDirectory);
        ConfigureNativeLibraries(appRoot);

        // Single instance per portable folder (cross-platform). Later starts forward their arguments,
        // e.g. "CaptureDoc-Linux --capture region" bound to a desktop shortcut.
        var env = PortableEnvironment.Detect();
        SingleInstance? instance = null;
        try
        {
            Directory.CreateDirectory(env.CacheDir);
            instance = new SingleInstance(env.CacheDir);
            if (!instance.IsFirst)
            {
                instance.Send(args);
                instance.Dispose();
                return 0;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            instance = null; // read-only medium: run without single-instance support
        }
        App.Instance = instance;
        App.StartupArgs = args;

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Services.Log.Error("Fatal", ex);
            throw;
        }
        finally { instance?.Dispose(); }
    }

    /// <summary>
    /// Native libraries live in &lt;AppRoot&gt;/Runtime/&lt;rid&gt;/ (Windows, Linux) so nothing is extracted to temp.
    /// On macOS they are inside the .app bundle next to the executable (default probing). See ADR-002.
    /// </summary>
    private static void ConfigureNativeLibraries(string appRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            CaptureDoc.Platform.Windows.NativeRuntimeBootstrap.Configure(appRoot, typeof(SkiaSharp.SKImage).Assembly, typeof(HarfBuzzSharp.Buffer).Assembly);
            return;
        }
        if (!OperatingSystem.IsLinux()) return;
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        var dir = Path.Combine(appRoot, "Runtime", arch);
        if (!Directory.Exists(dir)) return;
        foreach (var asm in new[] { typeof(SkiaSharp.SKImage).Assembly, typeof(HarfBuzzSharp.Buffer).Assembly })
        {
            try
            {
                NativeLibrary.SetDllImportResolver(asm, (name, _, _) =>
                {
                    foreach (var candidate in new[] { name, name + ".so", "lib" + name + ".so" })
                    {
                        var f = Path.Combine(dir, candidate);
                        if (File.Exists(f) && NativeLibrary.TryLoad(f, out var h)) return h;
                    }
                    return 0;
                });
            }
            catch (InvalidOperationException) { }
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
