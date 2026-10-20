using Avalonia;
using CaptureDoc.Core.Portable;

namespace CaptureDoc.App;

internal static class Program
{
    public const string InstanceEventName = "CaptureDoc.Portable.ShowQuickCapture";

    [STAThread]
    public static int Main(string[] args)
    {
        var appRoot = PortableEnvironment.FindAppRoot(AppContext.BaseDirectory);
        if (OperatingSystem.IsWindows())
            CaptureDoc.Platform.Windows.NativeRuntimeBootstrap.Configure(appRoot,
                typeof(SkiaSharp.SKImage).Assembly, typeof(HarfBuzzSharp.Buffer).Assembly);

        // Single instance per portable folder: a second start just shows the quick-capture bar.
        var name = InstanceEventName + "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(appRoot.ToLowerInvariant())))[..12];
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name, out var createdNew);
        if (!createdNew)
        {
            signal.Set();
            return 0;
        }
        App.InstanceSignal = signal;
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
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
