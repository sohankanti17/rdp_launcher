using System.Collections.ObjectModel;

namespace RdpLauncher;

public class Group : NotifyBase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnChanged(); }
    }

    public ObservableCollection<Profile> Profiles { get; set; } = new();
}
