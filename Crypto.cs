using System.Security.Cryptography;
using System.Text;

namespace RdpLauncher;

public static class Crypto
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public static string Unprotect(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        try
        {
            var enc = Convert.FromBase64String(b64);
            var bytes = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }

    // The value mstsc expects in "password 51:b:" — hex of the DPAPI-protected
    // UTF-16LE bytes of the password (current-user scope).
    public static string RdpPasswordHash(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var enc = ProtectedData.Protect(Encoding.Unicode.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        var sb = new StringBuilder(enc.Length * 2);
        foreach (var b in enc) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
