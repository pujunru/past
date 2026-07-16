using System.Security.Cryptography;

namespace Past.Infrastructure.Security;

/// <summary>
/// Supplies a stable per-install 256-bit content key. The key is generated once,
/// then persisted on disk wrapped with Windows DPAPI (CurrentUser scope) so the
/// stored bytes are unreadable if copied to another machine or user account.
/// </summary>
public sealed class DpapiKeyProvider
{
    private static readonly byte[] Entropy = "Past.content.key.v1"u8.ToArray();
    private readonly string _keyPath;
    private byte[]? _key;

    public DpapiKeyProvider(string keyPath) => _keyPath = keyPath;

    public byte[] GetKey()
    {
        if (_key is not null)
            return _key;

        if (File.Exists(_keyPath))
        {
            var wrapped = File.ReadAllBytes(_keyPath);
            _key = ProtectedData.Unprotect(wrapped, Entropy, DataProtectionScope.CurrentUser);
            return _key;
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedBytes = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        File.WriteAllBytes(_keyPath, protectedBytes);
        _key = key;
        return _key;
    }
}
