using System;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>The BYO transport key is distinct from the cloud account's CoreSecrets.ApiKey.</summary>
internal static class CompanionApiKey
{
    internal const string Name = "companion_openai";
    internal const string Reference = "secret:companion_openai";

    internal static string? SessionKey; // null: no override; empty: session-only clear

    internal static bool Store(string? key)
    {
        // WPF MainWindow.CompanionRoom.cs:315 returns early without settings.
        if (CoreSettings.Current is null) return false;
        // WPF MainWindow/MainWindow.CompanionRoom.cs:318 persists the protected value.
        // A failed OS write must leave that persisted value intact, including legacy blobs.
        var persisted = SecretStore.StoreWithStatus(Name, string.IsNullOrEmpty(key) ? null : key);
        SessionKey = persisted ? null : key ?? "";
        if (persisted)
            CoreSettings.Current.CompanionPrompt.OpenAiCompatibleApiKey =
                string.IsNullOrEmpty(key) ? "" : Reference;
        return persisted;
    }

    internal static string? Read(string stored)
    {
        if (SessionKey is { } session) return string.IsNullOrEmpty(session) ? null : session;
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
