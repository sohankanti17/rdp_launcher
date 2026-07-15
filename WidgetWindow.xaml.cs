using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
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
    private double _expandedHeight;
    private double _restingOpacity = 1.0;
    private bool _isAnimating = false;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private double _expandedWidth;
    private double _expandedLeft;
    private bool _isBubbled;
    private const double BubbleSize = 54;

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
        _expandedWidth = Width;
        _expandedLeft = Left;
        _restingOpacity = Math.Clamp(s.Opacity, 0.3, 1.0);
        Opacity = 1.0;  // always start fully visible
        _autoHide = s.AutoHide;
        UpdateAutoHideButton();

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_isBubbled) CollapseToBubble();
        };

        _updateTimer.Tick += (_, _) => CheckForUpdates();
        _updateTimer.Start();

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
            if (_isBubbled)
                ExpandFromBubble();
            else
            {
                MinHeight = 100;
                MinWidth = 100;
                if (Height < _expandedHeight) Height = _expandedHeight;
            }
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
        if (_autoHide && !_isBubbled) _hideTimer.Start();
        AnimateOpacity(Opacity, _restingOpacity);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _hideTimer.Stop();
        AnimateOpacity(Opacity, 1.0);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_isBubbled && !_isAnimating)
            ExpandFromBubble();
        base.OnMouseLeftButtonDown(e);
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

    // ── WM_NCHITTEST hook — makes the transparent window area click-through when bubbled ──

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        if (msg == WM_NCHITTEST && _isBubbled)
        {
            // lParam is screen coordinates in physical pixels
            int screenX = unchecked((short)(lParam.ToInt32() & 0xFFFF));
            int screenY = unchecked((short)((lParam.ToInt32() >> 16) & 0xFFFF));

            var dpi = VisualTreeHelper.GetDpi(this);
            int bLeft  = (int)((Left + Width - BubbleSize) * dpi.DpiScaleX);
            int bTop   = (int)(Top  * dpi.DpiScaleY);
            int bSize  = (int)(BubbleSize * dpi.DpiScaleX);

            bool inBubble = screenX >= bLeft && screenX < bLeft + bSize
                         && screenY >= bTop  && screenY < bTop  + bSize;
            if (!inBubble) { handled = true; return new IntPtr(HTTRANSPARENT); }
        }
        return IntPtr.Zero;
    }

    private void CollapseToBubble()
    {
        _isAnimating = true;
        _isBubbled = true;

        // Go blue + round immediately so widget shrinks as a filled circle
        OuterBorder.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        OuterBorder.BorderThickness = new Thickness(0);
        // CornerRadius must compensate for the ScaleTransform so the circle looks round.
        // Visual radius = CornerRadius * scale; to get BubbleSize/2 visual radius:
        // CornerRadius = (BubbleSize/2) / scale = Height/2 (using the smaller scale axis)
        OuterBorder.CornerRadius = new CornerRadius(Math.Max(Width, Height) / 2);
        MainContent.Visibility = Visibility.Collapsed;
        ResizeMode = ResizeMode.NoResize;

        // ScaleTransform anchored top-right: pure GPU, window never resizes during animation
        double scaleX = BubbleSize / Width;
        double scaleY = BubbleSize / Height;
        var scale = new ScaleTransform(1, 1);
        OuterBorder.RenderTransformOrigin = new Point(1, 0);
        OuterBorder.RenderTransform = scale;

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var dur = TimeSpan.FromMilliseconds(280);
        var animSX = new DoubleAnimation(1, scaleX, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var animSY = new DoubleAnimation(1, scaleY, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };

        animSX.Completed += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.ScaleX = scaleX;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleY = scaleY;
            BubbleContent.Visibility = Visibility.Visible;
            _isAnimating = false;
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animSX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animSY);
    }

    private void ExpandFromBubble()
    {
        _isAnimating = true;
        _isBubbled = false;  // cleared before animation so WM_NCHITTEST stops blocking

        BubbleContent.Visibility = Visibility.Collapsed;
        OuterBorder.SetResourceReference(Border.BackgroundProperty, "WidgetBg");
        OuterBorder.BorderThickness = new Thickness(1);
        OuterBorder.CornerRadius = new CornerRadius(6);
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MainContent.Visibility = Visibility.Visible;

        // Window is already at full size — just animate the ScaleTransform back to 1
        // No Win32 resize = no flash, no flicker
        var scale = OuterBorder.RenderTransform as ScaleTransform
                    ?? new ScaleTransform(BubbleSize / Width, BubbleSize / Height);
        OuterBorder.RenderTransformOrigin = new Point(1, 0);
        OuterBorder.RenderTransform = scale;

        double fromX = scale.ScaleX, fromY = scale.ScaleY;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var dur = TimeSpan.FromMilliseconds(280);
        var animSX = new DoubleAnimation(fromX, 1, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var animSY = new DoubleAnimation(fromY, 1, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };

        animSX.Completed += (_, _) =>
        {
            OuterBorder.RenderTransform = null;
            _isAnimating = false;
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animSX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animSY);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        if (!_isAnimating && Height > 50) _expandedHeight = Height;
        if (!_isAnimating && Width > 100) _expandedWidth = Width;
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
        _isBubbled = false;
        MainContent.Visibility = Visibility.Visible;
        BubbleContent.Visibility = Visibility.Collapsed;
        OuterBorder.CornerRadius = new CornerRadius(6);
        Width = 240; Height = 440;
        Left = wa.Right - Width - 12;
        Top = wa.Top + 20;
        _expandedHeight = 440;
        _expandedWidth = 240;
        _expandedLeft = Left;
        MinHeight = 100;
        MinWidth = 100;
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
            _updateTimer.Stop();
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
