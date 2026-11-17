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
            var data = File.ReadAllBytes(DockData.FilePath(name));
            var clear = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException) { return null; }
    }

    internal static void Save(AssistantProvider provider, string key) => WritePrivateText(FileName(provider), key);

    internal static void WritePrivateText(string name, string text)
    {
        var clear = Encoding.UTF8.GetBytes(text);
        try
        {
            var protectedKey = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            DockData.Write(name, path => File.WriteAllBytes(path, protectedKey));
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    internal static void Forget(AssistantProvider provider) => File.Delete(DockData.FilePath(FileName(provider)));
}
