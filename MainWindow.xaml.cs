using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace RdpLauncher;

public partial class MainWindow : Window
{
    private ProfileData _data = new();
    private Profile? _current;          // VM currently loaded in the detail panel
    private Group? _selectedGroup;      // group node currently selected in the tree

    public MainWindow()
    {
        InitializeComponent();
        _data = ((App)Application.Current).Data;
        Tree.ItemsSource = _data.Groups;
        CmbGroup.ItemsSource = _data.Groups;
        UpdateSilentStatus();
        EnableDetail(false);
    }

    // ---- Password show/hide -------------------------------------------------
    private string PwdValue =>
        TxtPwdShown.Visibility == Visibility.Visible ? TxtPwdShown.Text : PwdBox.Password;

    private void SetPwd(string v) { PwdBox.Password = v; TxtPwdShown.Text = v; }

    private void ChkShow_Checked(object sender, RoutedEventArgs e)
    {
        TxtPwdShown.Text = PwdBox.Password;
        TxtPwdShown.Visibility = Visibility.Visible;
        PwdBox.Visibility = Visibility.Collapsed;
    }

    private void ChkShow_Unchecked(object sender, RoutedEventArgs e)
    {
        PwdBox.Password = TxtPwdShown.Text;
        PwdBox.Visibility = Visibility.Visible;
        TxtPwdShown.Visibility = Visibility.Collapsed;
    }

    // ---- Tree selection -----------------------------------------------------
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case Profile p:
                _selectedGroup = OwnerOf(p);
                LoadProfile(p);
                break;
            case Group g:
                _selectedGroup = g;
                _current = null;
                EnableDetail(false);
                break;
            default:
                _selectedGroup = null;
                break;
        }
    }

    private Group? OwnerOf(Profile p) =>
        _data.Groups.FirstOrDefault(g => g.Profiles.Contains(p));

    private void LoadProfile(Profile p)
    {
        _current = p;
        EnableDetail(true);
        TxtName.Text = p.Name;
        TxtHost.Text = p.Host;
        TxtUser.Text = p.Username;
        SetPwd(Crypto.Unprotect(p.PasswordEnc));
        CmbGroup.SelectedItem = OwnerOf(p);
        ChkClip.IsChecked = p.Clipboard;
        ChkDrives.IsChecked = p.Drives;
        ChkPrn.IsChecked = p.Printers;
        ChkFull.IsChecked = p.FullScreen;
        ChkMultimon.IsChecked = p.AllMonitors;
        ChkSkipCert.IsChecked = p.SkipCertWarn;
    }

    private void EnableDetail(bool on)
    {
        DetailPanel.IsEnabled = on;
        if (!on)
        {
            TxtName.Text = TxtHost.Text = TxtUser.Text = "";
            SetPwd("");
            CmbGroup.SelectedItem = null;
            ChkClip.IsChecked = true;
            ChkDrives.IsChecked = ChkPrn.IsChecked = ChkFull.IsChecked =
                ChkMultimon.IsChecked = ChkSkipCert.IsChecked = false;
        }
    }

    // ---- Save / connect a VM ------------------------------------------------
    private bool SaveCurrent()
    {
        var name = TxtName.Text.Trim();
        var host = TxtHost.Text.Trim();
        var user = TxtUser.Text.Trim();

        if (host.Length == 0 || user.Length == 0)
        {
            MessageBox.Show("Host and Username are required.", "Missing info");
            return false;
        }
        if (name.Length == 0) name = host;

        var targetGroup = CmbGroup.SelectedItem as Group
                          ?? _selectedGroup
                          ?? _data.Groups.FirstOrDefault()
                          ?? _data.GetOrCreateGroup("My VMs");

        if (_current == null)
        {
            _current = new Profile();
            targetGroup.Profiles.Add(_current);
        }
        else
        {
            // Move between groups if the selection changed.
            var owner = OwnerOf(_current);
            if (owner != null && owner != targetGroup)
            {
                owner.Profiles.Remove(_current);
                targetGroup.Profiles.Add(_current);
            }
        }

        _current.Name = name;
        _current.Host = host;
        _current.Username = user;
        _current.PasswordEnc = Crypto.Protect(PwdValue);
        _current.Clipboard = ChkClip.IsChecked == true;
        _current.Drives = ChkDrives.IsChecked == true;
        _current.Printers = ChkPrn.IsChecked == true;
        _current.FullScreen = ChkFull.IsChecked == true;
        _current.AllMonitors = ChkMultimon.IsChecked == true;
        _current.SkipCertWarn = ChkSkipCert.IsChecked == true;

        ProfileStore.Save(_data);
        return true;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveCurrent();

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveCurrent()) return;
        if (_current != null) RdpService.Connect(_current);
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is Profile p) RdpService.Connect(p);
    }

    // ---- Group / VM management ---------------------------------------------
    private void BtnNewGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = Prompt.Show("Group name:", "New group");
        if (name == null) return;
        var g = new Group { Name = name };
        _data.Groups.Add(g);
        ProfileStore.Save(_data);
        _selectedGroup = g;
    }

    private void BtnNewVm_Click(object sender, RoutedEventArgs e)
    {
        if (_data.Groups.Count == 0) _data.Groups.Add(new Group { Name = "My VMs" });
        var target = _selectedGroup ?? _data.Groups.First();
        _current = null;
        EnableDetail(true);
        TxtName.Text = TxtHost.Text = TxtUser.Text = "";
        SetPwd("");
        CmbGroup.SelectedItem = target;
        ChkClip.IsChecked = true;
        TxtName.Focus();
    }

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is Group g)
        {
            var name = Prompt.Show("Group name:", "Rename group", g.Name);
            if (name == null) return;
            g.Name = name;
            ProfileStore.Save(_data);
        }
        else if (Tree.SelectedItem is Profile p)
        {
            var name = Prompt.Show("Display name:", "Rename VM", p.Name);
            if (name == null) return;
            p.Name = name;
            if (_current == p) TxtName.Text = name;
            ProfileStore.Save(_data);
        }
        else
        {
            MessageBox.Show("Select a group or VM to rename.", "Rename");
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is Profile p)
        {
            if (MessageBox.Show($"Delete VM '{p.Name}'?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            OwnerOf(p)?.Profiles.Remove(p);
            if (_current == p) { _current = null; EnableDetail(false); }
            ProfileStore.Save(_data);
        }
        else if (Tree.SelectedItem is Group g)
        {
            var msg = g.Profiles.Count > 0
                ? $"Delete group '{g.Name}' and its {g.Profiles.Count} VM(s)?"
                : $"Delete group '{g.Name}'?";
            if (MessageBox.Show(msg, "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _data.Groups.Remove(g);
            if (_selectedGroup == g) _selectedGroup = null;
            ProfileStore.Save(_data);
        }
        else
        {
            MessageBox.Show("Select a group or VM to delete.", "Delete");
        }
    }

    private void BtnConnectGroup_Click(object sender, RoutedEventArgs e)
    {
        var g = (Tree.SelectedItem as Group) ?? _selectedGroup;
        if (g == null) { MessageBox.Show("Select a group first.", "Connect group"); return; }
        if (g.Profiles.Count == 0) return;

        if (g.Profiles.Count > 3 &&
            MessageBox.Show($"Open {g.Profiles.Count} RDP sessions?", "Connect group",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        foreach (var p in g.Profiles.ToList()) RdpService.Connect(p);
    }

    // ---- Import -------------------------------------------------------------
    private void BtnImportRdp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import .rdp files",
            Filter = "RDP files (*.rdp)|*.rdp|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        var target = _selectedGroup ?? _data.GetOrCreateGroup("Imported");
        int added = 0;
        foreach (var path in dlg.FileNames)
        {
            try
            {
                var p = ImportService.FromRdpFile(path);
                if (p != null && AddIfNew(target, p)) added++;
            }
            catch { /* skip unreadable file */ }
        }
        ProfileStore.Save(_data);
        MessageBox.Show(
            $"Imported {added} VM(s) into '{target.Name}'.\n\nPasswords are not imported — set them on each VM before connecting.",
            "Import .rdp");
    }

    private void BtnImportRdg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import RDCMan .rdg files",
            Filter = "RDCMan files (*.rdg)|*.rdg|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        int added = 0, groups = 0;
        foreach (var path in dlg.FileNames)
        {
            try
            {
                foreach (var (groupName, profile) in ImportService.FromRdgFile(path))
                {
                    var before = _data.Groups.Count;
                    var g = _data.GetOrCreateGroup(string.IsNullOrWhiteSpace(groupName) ? "Imported" : groupName);
                    if (_data.Groups.Count > before) groups++;
                    if (AddIfNew(g, profile)) added++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read '{System.IO.Path.GetFileName(path)}':\n{ex.Message}", "Import RDCMan");
            }
        }
        ProfileStore.Save(_data);
        MessageBox.Show(
            $"Imported {added} VM(s) across {groups} new group(s).\n\nPasswords are not imported — set them on each VM before connecting.",
            "Import RDCMan");
    }

    // Adds a profile unless an entry with the same host already exists in the group.
    private static bool AddIfNew(Group g, Profile p)
    {
        if (g.Profiles.Any(x => string.Equals(x.Host, p.Host, StringComparison.OrdinalIgnoreCase)))
            return false;
        g.Profiles.Add(p);
        return true;
    }

    // ---- Silent connect (sign + trust) -------------------------------------
    private void BtnSetupSigning_Click(object sender, RoutedEventArgs e)
    {
        bool currentlyOn = SigningService.IsTrustConfigured();
        RelaunchElevated(currentlyOn ? "--remove-signing" : "--setup-signing");
        UpdateSilentStatus();
    }

    private void RelaunchElevated(string arg)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!, arg)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi)?.WaitForExit();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Action cancelled or failed:\n" + ex.Message, "RDP Launcher");
        }
    }

    private void UpdateSilentStatus()
    {
        if (SigningService.IsTrustConfigured())
        {
            TxtSilentStatus.Text = "Silent connect: ON. Files are signed and trusted; no publisher warning.";
            TxtSilentStatus.Foreground = Brushes.Green;
            BtnSetupSigning.Content = "Disable silent connect";
        }
        else
        {
            TxtSilentStatus.Text = "Silent connect: OFF. Windows still shows the publisher warning. Click to enable (admin needed once).";
            TxtSilentStatus.Foreground = Brushes.DarkOrange;
            BtnSetupSigning.Content = "Enable silent connect";
        }
    }
}
