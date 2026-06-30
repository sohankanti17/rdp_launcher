namespace RdpLauncher;

public class Profile : NotifyBase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnChanged(); }
    }

    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordEnc { get; set; } = "";   // DPAPI-protected, base64
    public bool Clipboard { get; set; } = true;
    public bool Drives { get; set; }
    public bool Printers { get; set; }
    public bool FullScreen { get; set; }
    public bool AllMonitors { get; set; }
    public bool SkipCertWarn { get; set; }
}
