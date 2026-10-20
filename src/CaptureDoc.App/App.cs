using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Portable;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Rendering;

namespace CaptureDoc.App;

public sealed class App : Application
{
    public static EventWaitHandle? InstanceSignal { get; set; }
    public static string[] StartupArgs { get; set; } = Array.Empty<string>();
    public static new App Current => (App)Application.Current!;

    public AppServices Services { get; private set; } = null!;
    public CaptureController Capture { get; private set; } = null!;
    public Video.RecordingController Recording { get; private set; } = null!;
    public bool IsExiting { get; private set; }
    public bool HasTray { get; private set; }
    private QuickCaptureWindow? _quick;
    private StudioWindow? _studio;
    private TrayIcon? _tray;

    public IReadOnlyList<Window> OpenWindows =>
        ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.Windows : Array.Empty<Window>();

    public Window? AnyWindow => _quick ?? OpenWindows.FirstOrDefault();

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Name = "CaptureDoc";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            // Keep the app (and unsaved work) alive; autosave/recovery still runs.
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
            Dispatcher.UIThread.Post(async () => await Dialogs.InfoAsync(null, "Unerwarteter Fehler",
                "Ein interner Fehler ist aufgetreten und wurde protokolliert (Data/Logs). Bitte Arbeit speichern.\n\n" + e.Exception.Message));
        };
        Dispatcher.UIThread.Post(async () => await StartAsync());
    }

    private async Task StartAsync()
    {
        var env = PortableEnvironment.Detect();
        var check = env.EnsureWritable();
        if (!check.IsWritable)
        {
            var a = await Dialogs.AskAsync(null, "Portabler Datenordner nicht beschreibbar",
                $"CaptureDoc kann im Datenordner nicht schreiben:\n{env.DataRoot}\n\n{check.Problem}\n\n" +
                "Mögliche Ursachen: schreibgeschützter USB-Stick, fehlende Rechte, voller Datenträger.\n\n" +
                "Du kannst stattdessen einen Ordner im Benutzerprofil verwenden (Einstellungen und Projekte liegen dann nicht auf dem Stick).",
                "Benutzerordner verwenden", "Beenden");
            if (a != "Benutzerordner verwenden") { Shutdown(); return; }
            env = PortableEnvironment.CreateUserFallback(env.AppRoot);
            var c2 = env.EnsureWritable();
            if (!c2.IsWritable) { await Dialogs.InfoAsync(null, "Fehler", c2.Problem ?? "Kein beschreibbarer Ordner gefunden."); Shutdown(); return; }
        }

        Services = new AppServices(env);
        Log.Info($"Start {ProjectFile.AppVersion} root={env.AppRoot} data={env.DataRoot} runtime={(OperatingSystem.IsWindows() ? CaptureDoc.Platform.Windows.NativeRuntimeBootstrap.RuntimeDirectory : null) ?? "default"}");
        Recovery.Dir = env.RecoveryDir;
        Capture = new CaptureController(Services);
        Recording = new Video.RecordingController(Services);
        ApplyTheme();
        SetupTray();

        _quick = new QuickCaptureWindow(Services);
        _quick.Opened += (_, _) => Log.Info($"UI ready {(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms after process start");
        RegisterHotkeys();
        if (!Services.Settings.Platform.StartMinimizedToTray || !HasTray) _quick.Show();

        StartInstanceListener();

        if (Services.StartupProblems.Count > 0)
            await Dialogs.InfoAsync(_quick.IsVisible ? _quick : null, "Hinweise", string.Join("\n\n", Services.StartupProblems));

        await OfferRecoveryAsync();

        foreach (var arg in StartupArgs.Where(File.Exists)) await OpenPathAsync(arg, _quick);
    }

    public void ApplyTheme()
    {
        RequestedThemeVariant = Services.Settings.Shared.Theme switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public void RegisterHotkeys()
    {
        var hk = Services.Hotkeys;
        if (hk is null) return;
        hk.UnregisterAll();
        Services.HotkeyStatus.Clear();
        foreach (var h in Services.Settings.Platform.Hotkeys)
        {
            var mode = h.Action switch
            {
                "window" => CaptureMode.ActiveWindow, "screen" => CaptureMode.FullScreen, "last-region" => CaptureMode.LastRegion, _ => CaptureMode.Region,
            };
            var ok = h.Action == "video"
                ? hk.Register(h.Action, h.Gesture, () => Dispatcher.UIThread.Post(async () => await Recording.StartAsync()))
                : hk.Register(h.Action, h.Gesture, () => Dispatcher.UIThread.Post(async () => await Capture.CaptureAsync(mode)));
            Services.HotkeyStatus[h.Action] = ok;
            if (!ok) Log.Warn($"Hotkey {h.Gesture} for {h.Action} could not be registered");
        }
        _quick?.UpdateHint();
    }

    private void SetupTray()
    {
        try
        {
            var menu = new NativeMenu();
            void Item(string text, Func<Task> action)
            {
                var i = new NativeMenuItem(text);
                i.Click += async (_, _) => await action();
                menu.Items.Add(i);
            }
            Item("Bereich aufnehmen", () => Capture.CaptureAsync(CaptureMode.Region));
            Item("Fenster aufnehmen", () => Capture.CaptureAsync(CaptureMode.Window));
            Item("Monitor aufnehmen", () => Capture.CaptureAsync(CaptureMode.Monitor));
            Item("Alle Bildschirme", () => Capture.CaptureAsync(CaptureMode.FullScreen));
            Item("Letzter Bereich", () => Capture.CaptureAsync(CaptureMode.LastRegion));
            Item("Bereich in 5 s", () => Capture.CaptureAsync(CaptureMode.Region, 5));
            Item("● Video aufnehmen", () => Recording.StartAsync());
            menu.Items.Add(new NativeMenuItemSeparator());
            Item("Quick Capture anzeigen", () => { ShowQuick(); return Task.CompletedTask; });
            Item("Documentation Studio", () => { ShowStudio(); return Task.CompletedTask; });
            Item("Datenordner öffnen", () => { OpenFolder(Services.Env.DataRoot); return Task.CompletedTask; });
            menu.Items.Add(new NativeMenuItemSeparator());
            Item("Beenden", () => { Exit(); return Task.CompletedTask; });

            _tray = new TrayIcon { Icon = IconFactory.WindowIcon, ToolTipText = "CaptureDoc – Klick: Bereich aufnehmen", Menu = menu, IsVisible = true };
            _tray.Clicked += async (_, _) => await Capture.CaptureAsync(CaptureMode.Region);
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
            HasTray = true;
        }
        catch (Exception ex)
        {
            Log.Warn("Tray unavailable: " + ex.Message);
            HasTray = false;
        }
    }

    private void StartInstanceListener()
    {
        var signal = InstanceSignal;
        if (signal is null) return;
        var t = new Thread(() =>
        {
            while (!IsExiting)
            {
                if (signal.WaitOne(1000)) Dispatcher.UIThread.Post(ShowQuick);
            }
        }) { IsBackground = true, Name = "CaptureDoc instance listener" };
        t.Start();
    }

    public void ShowQuick()
    {
        if (_quick is null) return;
        _quick.Show();
        _quick.WindowState = WindowState.Normal;
        _quick.Activate();
    }

    public StudioWindow ShowStudio()
    {
        if (_studio is null || !_studio.IsVisible)
        {
            _studio = new StudioWindow(Services);
            _studio.Closed += (_, _) => _studio = null;
            _studio.Show();
        }
        _studio.Activate();
        return _studio;
    }

    public async Task OpenFileAsync(Window owner)
    {
        var path = await Dialogs.OpenFileAsync(owner, "Öffnen", Services.Env.ProjectsDir,
            ("Projekte, Bilder und Videos", new[] { "*" + ProjectFile.Extension, "*.png", "*.jpg", "*.jpeg", "*.webp", "*.mp4", "*.mkv", "*.webm", "*.mov", "*.gif" }));
        if (path is not null) await OpenPathAsync(path, owner);
    }

    public async Task OpenPathAsync(string path, Window? owner)
    {
        try
        {
            if (VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                if (Services.Ffmpeg is null) { await Video.RecordingController.ShowMissingFfmpegAsync(owner); return; }
                new Video.VideoEditorWindow(Services, path).Show();
                return;
            }
            if (path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                var manifestPages = ProjectFile.Load(path, verifyAssets: false).Project;
                if (manifestPages.Guide.Steps.Count > 0 || manifestPages.Pages.Count != 1)
                {
                    ShowStudio().OpenFile(path);
                    return;
                }
                var s = ProjectSession.Open(path, out var warnings);
                new EditorWindow(Services, s, s.Project.Pages[0]).Show();
                if (warnings.Count > 0) await Dialogs.InfoAsync(owner, "Hinweise beim Öffnen", string.Join("\n", warnings));
                return;
            }
            var bytes = await File.ReadAllBytesAsync(path);
            var img = SkiaSharp.SKImage.FromEncodedData(bytes) ?? throw new InvalidDataException("Bildformat nicht erkannt.");
            var png = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? bytes : SkiaConvert.EncodePng(img);
            var session = ProjectSession.FromCapture(img, png, Path.GetFileNameWithoutExtension(path));
            new EditorWindow(Services, session, session.Project.Pages[0]).Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ProjectFormatException or UnauthorizedAccessException)
        {
            await Dialogs.InfoAsync(owner, "Öffnen fehlgeschlagen", ex.Message);
        }
    }

    private static readonly HashSet<string> VideoExtensions = new() { ".mp4", ".mkv", ".webm", ".mov", ".gif", ".avi" };

    private async Task OfferRecoveryAsync()
    {
        var pending = Recovery.Pending().ToList();
        if (pending.Count == 0) return;
        var list = string.Join("\n", pending.Select(p => $"• {p.when:g}  {(p.originalPath is null ? "(nie gespeichert)" : Path.GetFileName(p.originalPath))}"));
        var a = await Dialogs.AskAsync(_quick?.IsVisible == true ? _quick : null, "Wiederherstellung",
            $"CaptureDoc wurde nicht sauber beendet. Es gibt automatisch gesicherte Bearbeitungen:\n\n{list}\n\nJetzt öffnen?",
            "Wiederherstellen", "Verwerfen", "Später");
        foreach (var (file, origin, _) in pending)
        {
            if (a == "Wiederherstellen")
            {
                try
                {
                    var s = ProjectSession.OpenRecovery(file, origin);
                    if (s.Project.Guide.Steps.Count > 0 || s.Project.Pages.Count != 1) ShowStudio().UseSession(s);
                    else new EditorWindow(Services, s, s.Project.Pages[0]).Show();
                    try { File.Delete(file); File.Delete(file + ".origin"); } catch (IOException) { }
                }
                catch (ProjectFormatException ex) { Log.Warn("Recovery failed: " + ex.Message); }
            }
            else if (a == "Verwerfen")
            {
                try { File.Delete(file); File.Delete(file + ".origin"); } catch (IOException) { }
            }
        }
    }

    public void Notify(string message)
    {
        var w = new Window
        {
            WindowDecorations = WindowDecorations.None, Topmost = true, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight,
            Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 34)), CanResize = false,
            Content = new TextBlock { Text = message, Foreground = Brushes.White, Margin = new Thickness(16, 10), FontSize = 13 },
        };
        w.Opened += (_, _) =>
        {
            var screen = w.Screens.Primary;
            if (screen is not null)
            {
                var wa = screen.WorkingArea;
                w.Position = new PixelPoint(wa.Right - (int)(w.Bounds.Width * screen.Scaling) - 16, wa.Bottom - (int)(w.Bounds.Height * screen.Scaling) - 16);
            }
        };
        w.Show();
        DispatcherTimer.RunOnce(() => w.Close(), TimeSpan.FromSeconds(2.5));
    }

    private static void OpenFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Open folder: " + ex.Message); }
    }

    public void Exit()
    {
        IsExiting = true;
        foreach (var w in OpenWindows.ToList()) w.Close();
        // editors with unsaved changes cancel their close and ask; only quit when all are gone
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(300);
            if (OpenWindows.Any(w => w.IsVisible && w is EditorWindow or StudioWindow)) { IsExiting = false; return; }
            Shutdown();
        });
    }

    private void Shutdown()
    {
        IsExiting = true;
        if (_tray is not null) _tray.IsVisible = false;
        Services?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }
}
