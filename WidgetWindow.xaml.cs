using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace RdpLauncher;

public partial class WidgetWindow : Window
{
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
            // Dock to the right edge of the primary work area.
            var wa = SystemParameters.WorkArea;
            Width = 240;
            Height = Math.Min(460, wa.Height - 40);
            Left = wa.Right - Width - 12;
            Top = wa.Top + 20;
        }
    }

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

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is Profile p) RdpService.Connect(p);
    }

    private void MenuConnect_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is Profile p) RdpService.Connect(p);
    }

    private void BtnManager_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).OpenManager();

    private void BtnHide_Click(object sender, RoutedEventArgs e)
    {
        SaveBounds();
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveBounds();
        // Alt+F4 shouldn't kill the app — hide to tray instead, unless we're exiting.
        if (!((App)Application.Current).IsExiting)
        {
            e.Cancel = true;
            Hide();
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
            X = Left, Y = Top, W = Width, H = Height
        });
    }
}
