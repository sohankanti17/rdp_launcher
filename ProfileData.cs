using System.Collections.ObjectModel;

namespace RdpLauncher;

public class ProfileData
{
    public ObservableCollection<Group> Groups { get; set; } = new();

    // Find or create a group by name (case-insensitive).
    public Group GetOrCreateGroup(string name)
    {
        foreach (var g in Groups)
            if (string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
                return g;
        var grp = new Group { Name = name };
        Groups.Add(grp);
        return grp;
    }
}
