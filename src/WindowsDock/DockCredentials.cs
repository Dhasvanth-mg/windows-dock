using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WindowsDock;

internal static class DockCredentials
{
    private static string FileName(AssistantProvider provider) => $"provider-{provider}.dat";

    internal static string? Read(AssistantProvider provider) => ReadPrivateText(FileName(provider));

    internal static string? ReadPrivateText(string name)
    {
        try
        {
            return UnprotectText(File.ReadAllBytes(DockData.FilePath(name)));
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException) { return null; }
    }

    internal static void Save(AssistantProvider provider, string key) => WritePrivateText(FileName(provider), key);

    internal static void WritePrivateText(string name, string text)
    {
        var encrypted = ProtectText(text);
        DockData.Write(name, path => File.WriteAllBytes(path, encrypted));
    }

    internal static byte[] ProtectText(string text)
    {
        var clear = Encoding.UTF8.GetBytes(text);
        try { return ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    internal static string UnprotectText(byte[] encrypted)
    {
        var clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    internal static void Forget(AssistantProvider provider) => File.Delete(DockData.FilePath(FileName(provider)));
}
