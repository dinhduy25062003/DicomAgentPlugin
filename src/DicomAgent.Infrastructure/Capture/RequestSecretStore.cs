using System.Security.Cryptography;
using System.Text;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Services;

namespace DicomAgent.Infrastructure.Capture;

// Windows service credentials are protected by DPAPI. On Unix the encryption key is service-owner-only.
public sealed class RequestSecretStore : IRequestSecretStore
{
    private readonly string directory = Path.Combine(AgentPaths.DataDirectory, "requests");
    private byte[]? key;
    public string Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (OperatingSystem.IsWindows()) return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[bytes.Length]; var tag = new byte[16];
        using var aes = new AesGcm(Key(), 16); aes.Encrypt(nonce, bytes, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public string Unprotect(string value)
    {
        var bytes = Convert.FromBase64String(value);
        if (OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine));
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(Key(), 16); aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private byte[] Key()
    {
        if (key is not null) return key;
        var path = Path.Combine(directory, ".key");
        if (File.Exists(path)) return key = File.ReadAllBytes(path);
        var value = RandomNumberGenerator.GetBytes(32);
        using var file = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        file.Write(value); file.Flush(true);
        return key = value;
    }
}
