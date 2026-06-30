using System.Windows;
using System.Windows.Controls;

namespace RdpLauncher;

public static class Prompt
{
    public static string? Show(string message, string title, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(label, 0);

        var box = new TextBox { Text = initial, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(box, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(buttons, 2);

        string? result = null;
        var ok = new Button { Content = "OK", Width = 84, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 84, Height = 28, IsCancel = true };
        ok.Click += (_, __) => { result = box.Text.Trim(); win.DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        grid.Children.Add(label);
        grid.Children.Add(box);
        grid.Children.Add(buttons);
        win.Content = grid;

        win.Loaded += (_, __) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true && !string.IsNullOrWhiteSpace(result) ? result : null;
    }
}
