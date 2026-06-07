using System.IO;
using System.Security.Cryptography;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// The bearer key that gates the local API. Stored DPAPI-encrypted in apikey.bin via
/// <see cref="SecretStore"/> — deliberately NOT in settings.json, which the app (and
/// older builds) rewrite. Generating a new key instantly revokes the old one.
/// </summary>
public static class ApiKey
{
    private static string PathFor(AppSettings settings) =>
        Path.Combine(settings.DataDirectory, "apikey.bin");

    /// <summary>Returns the stored key, generating and persisting one on first use.</summary>
    public static string GetOrCreate(AppSettings settings)
    {
        var store = new SecretStore(PathFor(settings));
        var key = store.Load();
        if (string.IsNullOrEmpty(key))
        {
            key = Generate();
            store.Save(key);
        }
        return key;
    }

    /// <summary>Replaces the stored key with a fresh one and returns it.</summary>
    public static string Regenerate(AppSettings settings)
    {
        var key = Generate();
        new SecretStore(PathFor(settings)).Save(key);
        return key;
    }

    /// <summary>32 random bytes as URL-safe base64 (no padding) — safe inside an HTTP header.</summary>
    private static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
