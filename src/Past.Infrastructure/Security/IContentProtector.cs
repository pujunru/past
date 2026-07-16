namespace Past.Infrastructure.Security;

/// <summary>Encrypts/decrypts clip fields at rest.</summary>
public interface IContentProtector
{
    /// <summary>Encrypt plaintext, returning an opaque self-describing string (nonce+tag+ciphertext).</summary>
    string Protect(string plaintext);

    /// <summary>Reverse of <see cref="Protect"/>.</summary>
    string Unprotect(string payload);
}
