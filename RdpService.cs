using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RdpLauncher;

public static class RdpService
{
    private static readonly List<string> TempFiles = new();

    public static void SweepOldTemp()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), "rdpl_*.rdp"))
                TryDelete(f);
        }
        catch { /* best effort */ }
    }

    public static void Connect(Profile p)
    {
        var file = BuildRdpFile(p);
        TrySign(file);
        Process.Start(new ProcessStartInfo("mstsc.exe", $"\"{file}\"") { UseShellExecute = true });
    }

    private static string BuildRdpFile(Profile p)
    {
        var pwPlain = Crypto.Unprotect(p.PasswordEnc);
        var pwHash = Crypto.RdpPasswordHash(pwPlain);
        int auth = p.SkipCertWarn ? 0 : 2;

        var sb = new StringBuilder();
        sb.AppendLine($"full address:s:{p.Host}");
        sb.AppendLine($"username:s:{p.Username}");
        if (pwHash.Length > 0) sb.AppendLine($"password 51:b:{pwHash}");
        sb.AppendLine("prompt for credentials:i:0");
        sb.AppendLine("promptcredentialonce:i:1");
        sb.AppendLine("enablecredsspsupport:i:1");
        sb.AppendLine($"authentication level:i:{auth}");
        sb.AppendLine($"redirectclipboard:i:{(p.Clipboard ? 1 : 0)}");
        sb.AppendLine($"redirectprinters:i:{(p.Printers ? 1 : 0)}");
        sb.AppendLine($"drivestoredirect:s:{(p.Drives ? "*" : "")}");
        if (p.FullScreen)
        {
            sb.AppendLine("screen mode id:i:2");                 // full screen
            sb.AppendLine($"use multimon:i:{(p.AllMonitors ? 1 : 0)}");
        }
        else
        {
            sb.AppendLine("screen mode id:i:1");                 // windowed
            sb.AppendLine("use multimon:i:0");
            sb.AppendLine("desktopwidth:i:1600");               // initial window size
            sb.AppendLine("desktopheight:i:900");
        }
        // Make the remote session match the window/monitor at native resolution,
        // and follow it when resized (RDP 8.1+; all current Windows support this).
        sb.AppendLine("dynamic resolution:i:1");

        var path = Path.Combine(Path.GetTempPath(), $"rdpl_{Guid.NewGuid():N}.rdp");
        File.WriteAllText(path, sb.ToString(), Encoding.Unicode);
        TempFiles.Add(path);
        return path;
    }

    private static void TrySign(string file)
    {
        var cert = SigningService.FindCert();
        if (cert == null) return;

        var rdpsign = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "rdpsign.exe");
        if (!File.Exists(rdpsign)) return;

        if (!Run(rdpsign, $"/sha256 {cert.Thumbprint} \"{file}\""))
            Run(rdpsign, $"/sha1 {cert.Thumbprint} \"{file}\"");
    }

    private static bool Run(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Cleanup()
    {
        foreach (var f in TempFiles) TryDelete(f);
        TempFiles.Clear();
    }

    private static void TryDelete(string f)
    {
        try { if (File.Exists(f)) File.Delete(f); } catch { /* ignore */ }
    }
}
