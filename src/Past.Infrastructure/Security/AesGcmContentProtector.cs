using System.Security.Cryptography;
using System.Text;

namespace Past.Infrastructure.Security;

/// <summary>
/// AES-256-GCM field encryption. Output layout (base64): [12-byte nonce][16-byte tag][ciphertext].
/// The key comes from <see cref="DpapiKeyProvider"/>, so the plaintext is bound to the Windows user.
/// </summary>
public sealed class AesGcmContentProtector : IContentProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmContentProtector(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Key must be 256-bit (32 bytes).", nameof(key));
        _key = key;
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string payload)
    {
        var bytes = Convert.FromBase64String(payload);
        if (bytes.Length < NonceSize + TagSize)
            throw new CryptographicException("Payload too short.");

        var nonce = bytes.AsSpan(0, NonceSize);
        var tag = bytes.AsSpan(NonceSize, TagSize);
        var cipher = bytes.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
