using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace RdpLauncher;

public static class SigningService
{
    private const string Subject = "CN=RDP Launcher Signing";
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
    private const string ValueName = "TrustedCertThumbprints";

    public static X509Certificate2? FindCert()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        foreach (var c in store.Certificates)
            if (c.Subject == Subject && c.HasPrivateKey)
                return c;
        return null;
    }

    public static bool IsTrustConfigured()
    {
        var cert = FindCert();
        if (cert == null) return false;

        using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
        if (key?.GetValue(ValueName) is not string val || string.IsNullOrWhiteSpace(val))
            return false;

        return val.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                  .Contains(cert.Thumbprint, StringComparer.OrdinalIgnoreCase);
    }

    // Runs in the ELEVATED instance (relaunched with --setup-signing).
    public static void RunSetup()
    {
        var cert = FindCert() ?? CreateAndStoreCert();

        // Trust the public certificate so its signature validates.
        var pub = new X509Certificate2(cert.Export(X509ContentType.Cert));
        AddToStore(pub, StoreName.Root);
        AddToStore(pub, StoreName.TrustedPublisher);

        // Register the thumbprint as a trusted .rdp publisher (machine policy).
        using var key = Registry.LocalMachine.CreateSubKey(PolicyKey, writable: true)!;
        var existing = key.GetValue(ValueName) as string ?? "";
        var thumbs = existing
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (!thumbs.Contains(cert.Thumbprint, StringComparer.OrdinalIgnoreCase))
            thumbs.Add(cert.Thumbprint);
        key.SetValue(ValueName, string.Join(";", thumbs), RegistryValueKind.String);
    }

    // Removes the cert from all stores and clears the thumbprint from policy.
    public static void RemoveSetup()
    {
        var cert = FindCert();
        string? thumb = cert?.Thumbprint;

        RemoveFromStore(StoreName.My, thumb);
        RemoveFromStore(StoreName.Root, thumb);
        RemoveFromStore(StoreName.TrustedPublisher, thumb);

        using var key = Registry.LocalMachine.OpenSubKey(PolicyKey, writable: true);
        if (key?.GetValue(ValueName) is string val && thumb != null)
        {
            var remaining = val
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !t.Equals(thumb, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (remaining.Length == 0) key.DeleteValue(ValueName, throwOnMissingValue: false);
            else key.SetValue(ValueName, string.Join(";", remaining), RegistryValueKind.String);
        }
    }

    private static X509Certificate2 CreateAndStoreCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(Subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false)); // Code Signing EKU
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var ephemeral = req.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));

        // Re-import so the private key persists in the user's key store.
        var pfx = ephemeral.Export(X509ContentType.Pfx);
        var cert = new X509Certificate2(
            pfx, (string?)null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        return cert;
    }

    private static void AddToStore(X509Certificate2 cert, StoreName name)
    {
        using var store = new X509Store(name, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        if (store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count == 0)
            store.Add(cert);
    }

    private static void RemoveFromStore(StoreName name, string? thumbprint)
    {
        if (thumbprint == null) return;
        using var store = new X509Store(name, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        foreach (var c in matches) store.Remove(c);
    }
}
