using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RdpLauncher;

public partial class WidgetWindow : Window
{
    private bool _allExpanded = true;
    private bool _autoHide = false;
    private double _expandedHeight;
    private double _restingOpacity = 1.0;
    private bool _isAnimating = false;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };

    public WidgetWindow()
    {
        InitializeComponent();
        Tree.ItemsSource = ((App)Application.Current).Data.Groups;

        var s = SettingsStore.Load();
        if (s.HasPosition)
        {
            Left = s.X; Top = s.Y; Width = s.W; Height = s.H;
            EnsureOnScreen();
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Width = 240;
            Height = Math.Min(460, wa.Height - 40);
            Left = wa.Right - Width - 12;
            Top = wa.Top + 20;
        }

        _expandedHeight = Height;
        _restingOpacity = Math.Clamp(s.Opacity, 0.3, 1.0);
        Opacity = 1.0;  // always start fully visible
        _autoHide = s.AutoHide;
        UpdateAutoHideButton();

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            MinHeight = 0;
            AnimateHeight(30);
        };

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        CheckForUpdates();
    }

    // ── Animation ────────────────────────────────────────────────────────────

    private void AnimateHeight(double to)
    {
        _isAnimating = true;
        var anim = new DoubleAnimation(Height, to, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            _isAnimating = false;
            BeginAnimation(HeightProperty, null);
            Height = to;
            if (to > 50) MinHeight = 100;
        };
        BeginAnimation(HeightProperty, anim);
    }

    // ── Dark mode ────────────────────────────────────────────────────────────

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

    // ── Opacity (mouse wheel on header) ─────────────────────────────────────

    private void Header_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _restingOpacity = Math.Clamp(_restingOpacity + (e.Delta > 0 ? 0.05 : -0.05), 0.3, 1.0);
        SaveBounds();
    }

    // ── Auto-hide ────────────────────────────────────────────────────────────

    private void BtnAutoHide_Click(object sender, RoutedEventArgs e)
    {
        _autoHide = !_autoHide;
        if (!_autoHide)
        {
            _hideTimer.Stop();
            MinHeight = 100;
            if (Height < _expandedHeight) Height = _expandedHeight;
        }
        UpdateAutoHideButton();
        SaveBounds();
    }

    private void UpdateAutoHideButton()
    {
        BtnAutoHide.Content  = _autoHide ? "⇤" : "⇥";
        BtnAutoHide.ToolTip  = _autoHide ? "Disable auto-hide" : "Enable auto-hide";
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_autoHide) _hideTimer.Start();
        AnimateOpacity(Opacity, _restingOpacity);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _hideTimer.Stop();
        AnimateOpacity(Opacity, 1.0);
        if (_autoHide && Height < _expandedHeight)
            AnimateHeight(_expandedHeight);
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

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        // Keep _expandedHeight in sync when user manually resizes (not during animation).
        if (!_isAnimating && Height > 50) _expandedHeight = Height;
        base.OnRenderSizeChanged(info);
    }

    // ── Update check ─────────────────────────────────────────────────────────

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

    // ── Tree ─────────────────────────────────────────────────────────────────

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

    // ── Header buttons ───────────────────────────────────────────────────────

    private void EnsureOnScreen()
    {
        var va = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        if (Left < 0 || Top < 0 || Left > va - 40 || Top > vh - 40)
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 12;
            Top = wa.Top + 20;
        }
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        Width = 240; Height = 440;
        Left = wa.Right - Width - 12;
        Top = wa.Top + 20;
        _expandedHeight = 440;
        MinHeight = 100;
        SaveBounds();
    }

    private void BtnManager_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).OpenManager();

    private void BtnHide_Click(object sender, RoutedEventArgs e)
    {
        SaveBounds();
        Hide();
    }

    // ── Lifetime ─────────────────────────────────────────────────────────────

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
            X = Left, Y = Top, W = Width,
            H = Height > 50 ? Height : _expandedHeight,  // never save the collapsed height
            Opacity = _restingOpacity,
            AutoHide = _autoHide
        });
    }
}
