using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Mikeka.Api.Services;

public class EncryptionOptions
{
    /// <summary>Base64 of 32 random bytes (AES-256). Changing it makes stored passwords unreadable.</summary>
    public string Key { get; set; } = "";
}

/// <summary>
/// Encrypts bookmaker passwords with AES-256-GCM before they are stored in PostgreSQL.
/// The key comes from configuration ("Encryption:Key"), never from the database.
/// Stored format: "v1:" + base64(nonce[12] | ciphertext | tag[16]).
/// </summary>
public class AccountSecrets
{
    private const string Prefix = "v1:";
    private readonly byte[] _key;

    public AccountSecrets(IOptions<EncryptionOptions> options)
    {
        var k = options.Value.Key;
        try { _key = Convert.FromBase64String(k); }
        catch (FormatException) { _key = []; }
        if (_key.Length != 32)
            throw new InvalidOperationException(
                "Encryption:Key must be set in the config file to a base64 32-byte key " +
                "(generate one with: node -e \"console.log(require('crypto').randomBytes(32).toString('base64'))\").");
    }

    public string Protect(string password)
    {
        var plain = Encoding.UTF8.GetBytes(password);
        var blob = new byte[12 + plain.Length + 16];
        var nonce = blob.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, blob.AsSpan(12, plain.Length), blob.AsSpan(12 + plain.Length));
        return Prefix + Convert.ToBase64String(blob);
    }

    public string Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix)) throw new CryptographicException("Unknown password format.");
        var blob = Convert.FromBase64String(stored[Prefix.Length..]);
        var cipherLen = blob.Length - 12 - 16;
        var plain = new byte[cipherLen];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(12, cipherLen), blob.AsSpan(12 + cipherLen), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
