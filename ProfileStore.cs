using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace RdpLauncher;

public static class ProfileStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpLauncher");

    private static readonly string FilePath = Path.Combine(Dir, "profiles.json");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static ProfileData Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ProfileData();
            var json = File.ReadAllText(FilePath);
            var trimmed = json.TrimStart();

            // Legacy format (v1) was a bare JSON array of profiles — migrate it.
            if (trimmed.StartsWith("["))
            {
                var profiles = JsonSerializer.Deserialize<ObservableCollection<Profile>>(json)
                               ?? new ObservableCollection<Profile>();
                var data = new ProfileData();
                var g = new Group { Name = "My VMs" };
                foreach (var p in profiles) g.Profiles.Add(p);
                data.Groups.Add(g);
                return data;
            }

            return JsonSerializer.Deserialize<ProfileData>(json) ?? new ProfileData();
        }
        catch
        {
            return new ProfileData();
        }
    }

    public static void Save(ProfileData data)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Opts));
    }
}
