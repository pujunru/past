using System.Security.Cryptography;
using System.Text;

namespace Past.Infrastructure.Security;

/// <summary>
/// AES-256-GCM field encryption. Output layout: [12-byte nonce][16-byte tag][ciphertext],
/// base64-encoded for text columns and raw for BLOB columns.
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

    public string Protect(string plaintext) =>
        Convert.ToBase64String(ProtectBytes(Encoding.UTF8.GetBytes(plaintext)));

    public string Unprotect(string payload) =>
        Encoding.UTF8.GetString(UnprotectBytes(Convert.FromBase64String(payload)));

    public byte[] ProtectBytes(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return output;
    }

    public byte[] UnprotectBytes(byte[] payload)
    {
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Payload too short.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
