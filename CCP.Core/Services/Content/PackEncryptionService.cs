using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Handles AES encryption/decryption for content packs.
/// Uses machine-bound key derivation to prevent simple file copying.
/// </summary>
public static class PackEncryptionService
{
    // Salt for key derivation (constant, adds complexity)
    private static readonly byte[] Salt = new byte[]
    {
        0x43, 0x6F, 0x6E, 0x64, 0x69, 0x74, 0x69, 0x6F,
        0x6E, 0x69, 0x6E, 0x67, 0x50, 0x61, 0x6E, 0x65
    };

    // App-specific secret (part of the key)
    private const string AppSecret = "CCP_Pack_v1_2024";

    /// <summary>
    /// Derives a machine-bound encryption key.
    /// This makes copied pack folders useless on other machines.
    /// </summary>
    // Derived ONCE per process. It used to be re-derived (100,000 PBKDF2 rounds) for every
    // file: every pack flash picture paid for it on the way to the screen.
    private static readonly Lazy<byte[]> CachedKey = new(DeriveKeyCore);

    private static byte[] DeriveKey() => CachedKey.Value;

    private static byte[] DeriveKeyCore()
    {
        // Combine app secret with machine-specific data
        var machineId = Environment.MachineName + Environment.UserName;
        var combined = AppSecret + machineId;

        using var deriveBytes = new Rfc2898DeriveBytes(
            combined,
            Salt,
            100000, // iterations
            HashAlgorithmName.SHA256);

        return deriveBytes.GetBytes(32); // 256-bit key
    }

    /// <summary>
    /// Encrypts data using AES-256-CBC
    /// </summary>
    public static byte[] Encrypt(byte[] plainData)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(plainData, 0, plainData.Length);

        // Prepend IV to encrypted data (IV is not secret)
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return result;
    }

    /// <summary>
    /// Decrypts data using AES-256-CBC
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Extract IV from beginning of data
        var iv = new byte[16];
        Buffer.BlockCopy(encryptedData, 0, iv, 0, 16);
        aes.IV = iv;

        // Decrypt the rest
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encryptedData, 16, encryptedData.Length - 16);
    }

    /// <summary>
    /// Encrypts a file and saves with .enc extension
    /// </summary>
    /// STREAMED, same bytes on disk as <see cref="Encrypt"/> (IV, then AES-256-CBC). The old
    /// version held the file three times over in memory (plain, cipher, IV + cipher).
    public static void EncryptFile(string sourcePath, string destPath)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.SequentialScan);
        using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes);
        output.Write(aes.IV, 0, aes.IV.Length);
        using var encryptor = aes.CreateEncryptor();
        using var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write);
        input.CopyTo(crypto, CopyBufferBytes);
        crypto.FlushFinalBlock();
    }

    private const int CopyBufferBytes = 1 << 20;

    /// <summary>
    /// Decrypts a pack file into <paramref name="destination"/> a megabyte at a time.
    ///
    /// NEVER read a pack file whole. Until 7.1.8 every decrypt was File.ReadAllBytes plus a
    /// second full-size array from TransformFinalBlock, so a pack VIDEO cost twice its size in
    /// managed memory on every play (ccp-bugs #1441 / #1442: private bytes 1.4 GB to 4.0 GB and a
    /// 1.8 GB managed heap a minute after a pack video started; the whole PC crawled), and a
    /// video past 1 GB could not be decrypted at all.
    /// </summary>
    public static void DecryptFileTo(string encryptedPath, Stream destination)
    {
        using var input = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.SequentialScan);
        var iv = new byte[16];
        input.ReadExactly(iv);

        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var crypto = new CryptoStream(input, decryptor, CryptoStreamMode.Read);
        crypto.CopyTo(destination, CopyBufferBytes);
    }

    /// <summary>Decrypts a pack file straight to a file on disk (what a video needs). A failed
    /// decrypt leaves no half-written file behind.</summary>
    public static void DecryptFileToFile(string encryptedPath, string destPath)
    {
        try
        {
            using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes);
            DecryptFileTo(encryptedPath, output);
        }
        catch
        {
            try { File.Delete(destPath); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Decrypts a file to memory (never writes plaintext to disk). For SMALL files (a manifest,
    /// a picture): a video goes through <see cref="DecryptFileToFile"/>.
    /// </summary>
    public static byte[] DecryptFile(string encryptedPath)
    {
        using var stream = DecryptFileToStream(encryptedPath);
        return stream.ToArray();
    }

    /// <summary>
    /// Decrypts a file and returns as a memory stream (for image loading). One buffer, sized up
    /// front from the file length.
    /// </summary>
    public static MemoryStream DecryptFileToStream(string encryptedPath)
    {
        long length = new FileInfo(encryptedPath).Length;
        var stream = new MemoryStream((int)Math.Clamp(length - 16, 0, int.MaxValue / 2));
        DecryptFileTo(encryptedPath, stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Generates a random filename hash for obfuscation
    /// </summary>
    public static string GenerateObfuscatedFilename()
    {
        var bytes = new byte[8];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Encrypts and saves a manifest JSON
    /// </summary>
    public static void SaveEncryptedManifest(string json, string destPath)
    {
        var plainData = Encoding.UTF8.GetBytes(json);
        var encrypted = Encrypt(plainData);
        File.WriteAllBytes(destPath, encrypted);
    }

    /// <summary>
    /// Loads and decrypts a manifest JSON
    /// </summary>
    public static string LoadEncryptedManifest(string encryptedPath)
    {
        var encrypted = File.ReadAllBytes(encryptedPath);
        var decrypted = Decrypt(encrypted);
        return Encoding.UTF8.GetString(decrypted);
    }
}
