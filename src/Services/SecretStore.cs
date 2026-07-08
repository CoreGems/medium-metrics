using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MediumMetrics.Services;

/// <summary>
/// Encrypts/decrypts a small secret (the Medium session cookie string) at rest
/// using Windows DPAPI scoped to the current user. The ciphertext is only
/// decryptable by the same Windows user account on the same machine.
/// </summary>
public sealed class SecretStore
{
    // Extra entropy mixed into DPAPI so this blob can't be decrypted by another
    // app that happens to call ProtectedData on the same user's behalf.
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("MediumMetrics.session.v1");

    private readonly string _path;

    public SecretStore(string path) => _path = path;

    public bool Exists => File.Exists(_path);

    /// <summary>Encrypts and writes the secret, creating the directory if needed.</summary>
    public void Save(string secret)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] plain = Encoding.UTF8.GetBytes(secret);
        byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, cipher);
    }

    /// <summary>
    /// Reads and decrypts the secret, or returns null if it is missing or can no
    /// longer be decrypted (e.g. copied to another machine, or corrupted).
    /// </summary>
    public string? Load()
    {
        if (!Exists) return null;
        try
        {
            byte[] cipher = File.ReadAllBytes(_path);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Deletes the stored secret (used by "clear session" / re-login).</summary>
    public void Clear()
    {
        if (Exists) File.Delete(_path);
    }
}
