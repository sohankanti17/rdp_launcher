using System.Linq;
using System.Threading;
using System.Windows;

namespace RdpLauncher;

public partial class App : Application
{
    public ProfileData Data { get; private set; } = new();
    public bool IsExiting { get; private set; }

    private System.Windows.Forms.NotifyIcon? _tray;
    private WidgetWindow? _widget;
    private MainWindow? _manager;
    private Mutex? _mutex;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        // Elevated helper branches: do privileged work and exit.
        if (e.Args.Contains("--setup-signing"))  { RunElevated(SigningService.RunSetup, "enable");   return; }
        if (e.Args.Contains("--remove-signing")) { RunElevated(SigningService.RemoveSetup, "disable"); return; }

        // Single instance.
        _mutex = new Mutex(true, "RdpLauncher_SingleInstance_v1", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        // The app lives in the tray; closing windows must not exit it.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        RdpService.SweepOldTemp();
        Data = ProfileStore.Load();
        if (Data.Groups.Count == 0) Data.Groups.Add(new Group { Name = "My VMs" });

        SetupTray();
        ShowWidget();

        // First run with nothing saved yet -> open the manager so it's not empty.
        if (Data.Groups.All(g => g.Profiles.Count == 0)) OpenManager();
    }

    public void ShowWidget()
    {
        _widget ??= new WidgetWindow();
        _widget.Show();
        _widget.Activate();
    }

    public void ToggleWidget()
    {
        if (_widget is { IsVisible: true }) _widget.Hide();
        else ShowWidget();
    }

    public void OpenManager()
    {
        if (_manager == null)
        {
            _manager = new MainWindow();
            _manager.Closed += (_, __) => _manager = null;
        }
        _manager.Show();
        if (_manager.WindowState == WindowState.Minimized) _manager.WindowState = WindowState.Normal;
        _manager.Activate();
    }

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "RDP Launcher",
            Visible = true,
            Icon = LoadTrayIcon()
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open manager", null, (_, __) => OpenManager());
        menu.Items.Add("Show / hide widget", null, (_, __) => ToggleWidget());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, __) => ExitApp());
        _tray.ContextMenuStrip = menu;

        _tray.DoubleClick += (_, __) => ToggleWidget();
    }

    private System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (info?.Stream != null) return new System.Drawing.Icon(info.Stream);
        }
        catch { /* fall through */ }
        return System.Drawing.SystemIcons.Application;
    }

    private void ExitApp()
    {
        IsExiting = true;
        try { RdpService.Cleanup(); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Shutdown();
    }

    private void RunElevated(Action action, string label)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not {label} silent connect:\n{ex.Message}",
                "RDP Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Shutdown();
    }
}
