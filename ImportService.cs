using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RdpLauncher;

public static class ImportService
{
    // -------- .rdp files ----------------------------------------------------
    // Returns null if the file has no usable host. Passwords are never imported.
    public static Profile? FromRdpFile(string path)
    {
        var p = new Profile { Name = Path.GetFileNameWithoutExtension(path), Clipboard = true };

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            // Each setting is "key:type:value" where type is one of s, i, b.
            var m = Regex.Match(line, @"^(.*?):([sib]):(.*)$");
            if (!m.Success) continue;

            var key = m.Groups[1].Value.Trim().ToLowerInvariant();
            var val = m.Groups[3].Value;

            switch (key)
            {
                case "full address":      p.Host = val; break;
                case "username":          p.Username = val; break;
                case "redirectclipboard": p.Clipboard = val == "1"; break;
                case "redirectprinters":  p.Printers = val == "1"; break;
                case "drivestoredirect":  p.Drives = !string.IsNullOrWhiteSpace(val); break;
                case "screen mode id":    p.FullScreen = val == "2"; break;
                case "use multimon":      p.AllMonitors = val == "1"; break;
            }
        }

        if (string.IsNullOrWhiteSpace(p.Host)) return null;
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = p.Host;
        return p;
    }

    // -------- RDCMan .rdg files --------------------------------------------
    // Returns (groupName, profile) pairs. Nested groups are flattened to
    // "Parent / Child" names. Passwords are never imported.
    public static List<(string GroupName, Profile Profile)> FromRdgFile(string path)
    {
        var results = new List<(string, Profile)>();
        var doc = XDocument.Load(path);
        var file = doc.Root?.Element("file");
        if (file == null) return results;

        var credProfiles = ParseCredProfiles(file);
        var fileName = file.Element("properties")?.Element("name")?.Value
                       ?? Path.GetFileNameWithoutExtension(path);

        var fileUser = ParseLogon(file, credProfiles);

        // Servers directly under the file root -> a group named after the file.
        foreach (var s in file.Elements("server"))
            results.Add((fileName, ServerToProfile(s, fileUser, credProfiles)));

        // Top-level groups (recurse).
        foreach (var g in file.Elements("group"))
            WalkGroup(g, "", fileUser, credProfiles, results);

        return results;
    }

    private static void WalkGroup(
        XElement g, string parentPath, string? inheritedUser,
        Dictionary<string, string> credProfiles, List<(string, Profile)> results)
    {
        var name = g.Element("properties")?.Element("name")?.Value ?? "Group";
        var path = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath} / {name}";
        var groupUser = ParseLogon(g, credProfiles) ?? inheritedUser;

        foreach (var s in g.Elements("server"))
            results.Add((path, ServerToProfile(s, groupUser, credProfiles)));

        foreach (var sub in g.Elements("group"))
            WalkGroup(sub, path, groupUser, credProfiles, results);
    }

    private static Profile ServerToProfile(
        XElement s, string? inheritedUser, Dictionary<string, string> credProfiles)
    {
        var props = s.Element("properties");
        var host = props?.Element("name")?.Value ?? "";
        var display = props?.Element("displayName")?.Value;
        var user = ParseLogon(s, credProfiles) ?? inheritedUser ?? "";

        return new Profile
        {
            Name = string.IsNullOrWhiteSpace(display) ? host : display,
            Host = host,
            Username = user,
            Clipboard = true
        };
    }

    // Resolves a username (domain\user) from a logonCredentials element, or a
    // referenced named credentials profile. Returns null to mean "inherit".
    private static string? ParseLogon(XElement parent, Dictionary<string, string> credProfiles)
    {
        var lc = parent.Element("logonCredentials");
        if (lc == null) return null;

        var user = lc.Element("userName")?.Value;
        var domain = lc.Element("domain")?.Value;
        if (!string.IsNullOrWhiteSpace(user))
            return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";

        var profName = lc.Element("profileName")?.Value;
        if (!string.IsNullOrWhiteSpace(profName) && credProfiles.TryGetValue(profName, out var u))
            return u;

        return null;
    }

    private static Dictionary<string, string> ParseCredProfiles(XElement file)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cps = file.Element("credentialsProfiles")?.Elements("credentialsProfile")
                  ?? Enumerable.Empty<XElement>();

        foreach (var cp in cps)
        {
            var name = cp.Element("profileName")?.Value;
            var user = cp.Element("userName")?.Value;
            var dom = cp.Element("domain")?.Value;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(user))
                d[name] = string.IsNullOrWhiteSpace(dom) ? user : $"{dom}\\{user}";
        }
        return d;
    }
}
