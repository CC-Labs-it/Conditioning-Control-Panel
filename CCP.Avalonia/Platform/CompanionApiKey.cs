using System;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>The BYO transport key is distinct from the cloud account's CoreSecrets.ApiKey.</summary>
internal static class CompanionApiKey
{
    internal const string Name = "companion_openai";
    internal const string Reference = "secret:companion_openai";

    internal static void Store(string? key)
    {
        // Store first: a store failure must not replace the last usable settings value.
        CoreSecrets.StoreOrThrow(Name, string.IsNullOrEmpty(key) ? null : key);
        CoreSettings.Current.CompanionPrompt.OpenAiCompatibleApiKey =
            string.IsNullOrEmpty(key) ? "" : Reference;
    }

    internal static string? Read(string stored)
    {
        if (stored == Reference) return CoreSecrets.Retrieve(Name);
        if (string.IsNullOrEmpty(stored)) return null;
        // WPF SecureStringHelper uses base64 DPAPI with null entropy. Preserve its legacy
        // plaintext fallback only for non-base64 values, never for an unreadable encrypted blob.
        byte[] bytes;
        try { bytes = Convert.FromBase64String(stored); }
        catch (FormatException) { return stored; }
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { return null; }
    }
}
