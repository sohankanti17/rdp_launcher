using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RdpLauncher;

public partial class WidgetWindow : Window
{
    private bool _allExpanded = true;
    private bool _autoHide = false;
    private double _restingOpacity = 1.0;
    private bool _isAnimating = false;
    private bool _isDocked = false;
    private const double TabWidth = 20;

    private readonly DispatcherTimer _hideTimer   = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _desktopTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private IVirtualDesktopManager? _desktopManager;

    public WidgetWindow()
    {
        InitializeComponent();
        Tree.ItemsSource = ((App)Application.Current).Data.Groups;

        var s = SettingsStore.Load();
        _restingOpacity = Math.Clamp(s.Opacity, 0.3, 1.0);
        _autoHide = s.AutoHide;
        Opacity = 1.0;
        UpdateAutoHideButton();

        MaxHeight = SystemParameters.WorkArea.Height - 40;
        SnapToRightEdge();

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_isDocked) SlideToDock();
        };

        _updateTimer.Tick += (_, _) => CheckForUpdates();
        _updateTimer.Start();

        // Use documented IVirtualDesktopManager COM to follow the user across desktops.
        // Polling every 600ms: when the window is not on the current desktop, move it there.
        try
        {
            _desktopManager = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
            _desktopTimer.Tick += FollowCurrentDesktop;
            _desktopTimer.Start();
        }
        catch { /* COM not available — skip desktop following */ }

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        CheckForUpdates();
    }

    // ── Virtual desktop follow ────────────────────────────────────────────────

    [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    class VirtualDesktopManagerClass { }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hWnd, out bool onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(IntPtr hWnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hWnd, ref Guid desktopId);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private void FollowCurrentDesktop(object? s, EventArgs e)
    {
        if (_desktopManager == null) return;
        var ourHwnd = new WindowInteropHelper(this).Handle;
        if (ourHwnd == IntPtr.Zero) return;

        if (_desktopManager.IsWindowOnCurrentVirtualDesktop(ourHwnd, out bool onCurrent) != 0 || onCurrent)
            return;

        // Window is on a different desktop — find the current one via the foreground window
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ourHwnd) return;
        if (_desktopManager.GetWindowDesktopId(fg, out var desktopId) == 0 && desktopId != Guid.Empty)
            _desktopManager.MoveWindowToDesktop(ourHwnd, ref desktopId);
    }

    // ── Positioning ───────────────────────────────────────────────────────────

    private void SnapToRightEdge()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width;
        Top  = wa.Top + (wa.Height - ActualHeight) / 2;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        if (info.HeightChanged && !_isDocked) SnapToRightEdge();
        base.OnRenderSizeChanged(info);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == VisibilityProperty && (Visibility)e.NewValue == Visibility.Visible)
        {
            if (_isDocked) { _isDocked = false; PullTabArrow.Text = "▶"; }
            SnapToRightEdge();
            Opacity = 1.0;
        }
    }

    // ── Dark mode ─────────────────────────────────────────────────────────────

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Dispatcher.Invoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        bool dark = IsWindowsDarkMode();
        Resources["WidgetBg"]          = new SolidColorBrush(dark ? Color.FromRgb(30, 30, 30)   : Colors.White);
        Resources["WidgetBorderBrush"] = new SolidColorBrush(dark ? Color.FromRgb(68, 68, 68)   : Color.FromRgb(204, 204, 204));
        Resources["WidgetFg"]          = new SolidColorBrush(dark ? Colors.White                 : Color.FromRgb(30, 30, 30));
        Resources["WidgetMuted"]       = new SolidColorBrush(dark ? Color.FromRgb(160, 160, 160) : Colors.Gray);
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch { return false; }
    }

    // ── Opacity (mouse wheel on header) ──────────────────────────────────────

    private void Header_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _restingOpacity = Math.Clamp(_restingOpacity + (e.Delta > 0 ? 0.05 : -0.05), 0.3, 1.0);
        SaveBounds();
    }

    // ── Auto-hide / slide-to-edge ─────────────────────────────────────────────

    private void PullTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isAnimating) return;
        if (_isDocked) SlideOut();
        else SlideToDock();
        e.Handled = true;
    }

    private void BtnAutoHide_Click(object sender, RoutedEventArgs e)
    {
        _autoHide = !_autoHide;
        if (!_autoHide)
        {
            _hideTimer.Stop();
            if (_isDocked) SlideOut();
        }
        UpdateAutoHideButton();
        SaveBounds();
    }

    private void UpdateAutoHideButton()
    {
        BtnAutoHide.Content = _autoHide ? "⇤" : "⇥";
        BtnAutoHide.ToolTip = _autoHide ? "Disable auto-hide" : "Enable auto-hide";
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_autoHide && !_isDocked) _hideTimer.Start();
        AnimateOpacity(Opacity, _isDocked ? 0.5 : _restingOpacity);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _hideTimer.Stop();
        AnimateOpacity(Opacity, 1.0);
        base.OnMouseEnter(e);
    }

    private void AnimateOpacity(double from, double to)
    {
        if (Math.Abs(from - to) < 0.01) return;
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) => { BeginAnimation(OpacityProperty, null); Opacity = to; };
        BeginAnimation(OpacityProperty, anim);
    }

    private void SlideToDock()
    {
        _isAnimating = true;
        _isDocked = true;
        PullTabArrow.Text = "◀";

        var wa = SystemParameters.WorkArea;
        double target = wa.Right - TabWidth;

        var anim = new DoubleAnimation(Left, target, TimeSpan.FromMilliseconds(350))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop };

        anim.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            Left = target;
            AnimateOpacity(Opacity, 0.5);
            _isAnimating = false;
        };
        BeginAnimation(LeftProperty, anim);
    }

    private void SlideOut()
    {
        _isAnimating = true;
        _isDocked = false;
        PullTabArrow.Text = "▶";
        AnimateOpacity(Opacity, 1.0);

        var wa = SystemParameters.WorkArea;
        double target = wa.Right - Width;

        var anim = new DoubleAnimation(Left, target, TimeSpan.FromMilliseconds(350))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop };

        anim.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            Left = target;
            _isAnimating = false;
        };
        BeginAnimation(LeftProperty, anim);
    }

    // ── Update check ──────────────────────────────────────────────────────────

    private async void CheckForUpdates()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "RdpLauncher");
            var json = await client.GetStringAsync(
                "https://api.github.com/repos/sohankanti17/rdp_launcher/releases/latest");

            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latest = Version.Parse(tag.TrimStart('v'));
            var current = Assembly.GetExecutingAssembly().GetName().Version!;

            if (latest > current)
            {
                UpdateText.Text = $"⬆ v{latest.ToString(3)} available — click to download";
                UpdateBanner.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    private void UpdateBanner_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo(
            "https://github.com/sohankanti17/rdp_launcher/releases/latest")
        { UseShellExecute = true });
    }

    // ── Tree ──────────────────────────────────────────────────────────────────

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is Profile p) RdpService.Connect(p);
    }

    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item != null) item.IsSelected = true;
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T t) return t;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private void MenuConnect_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is Profile p) RdpService.Connect(p);
    }

    private void MenuCopyCreds_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is not Profile p) return;
        var pwd = Crypto.Unprotect(p.PasswordEnc);
        Clipboard.SetText($"Host: {p.Host}\nUsername: {p.Username}\nPassword: {pwd}");
    }

    private void BtnToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        _allExpanded = !_allExpanded;
        foreach (var group in ((App)Application.Current).Data.Groups)
        {
            if (Tree.ItemContainerGenerator.ContainerFromItem(group) is TreeViewItem item)
                item.IsExpanded = _allExpanded;
        }
        BtnToggleExpand.Content = _allExpanded ? "▼" : "▶";
        BtnToggleExpand.ToolTip = _allExpanded ? "Collapse all groups" : "Expand all groups";
    }

    // ── Header buttons ────────────────────────────────────────────────────────

    private void BtnManager_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).OpenManager();

    private void BtnHide_Click(object sender, RoutedEventArgs e)
    {
        SaveBounds();
        Hide();
    }

    // ── Lifetime ──────────────────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveBounds();
        if (!((App)Application.Current).IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _updateTimer.Stop();
            _desktopTimer.Stop();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        base.OnClosing(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        SaveBounds();
        base.OnDeactivated(e);
    }

    private void SaveBounds()
    {
        if (WindowState != WindowState.Normal) return;
        SettingsStore.Save(new WidgetSettings
        {
            HasPosition = true,
            X = Left, Y = Top, W = Width, H = Height,
            Opacity = _restingOpacity,
            AutoHide = _autoHide
        });
    }
}
