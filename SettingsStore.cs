using System.IO;
using System.Text.Json;

namespace RdpLauncher;

public class WidgetSettings
{
    public bool HasPosition { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 240;
    public double H { get; set; } = 440;
}

public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpLauncher");

    private static readonly string FilePath = Path.Combine(Dir, "widget.json");

    public static WidgetSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(FilePath)) ?? new WidgetSettings()
                : new WidgetSettings();
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public static void Save(WidgetSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
        }
        catch { /* best effort */ }
    }
}
