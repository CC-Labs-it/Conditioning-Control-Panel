using System;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF MainWindow.CompanionRoom.cs:312-321 and App.xaml.cs:659.
/// Windows settings use WPF DPAPI blobs; other platforms use the OS store. No new plaintext fallback.</summary>
internal static class CompanionApiKey
{
    internal const string Slot = "companion_byo_key";
    internal const string Reference = "ccp-secret:companion-byo-v1";

    internal static void Seed() => OpenAiCompatibleService.ApiKeyUnprotect = Read;

    // Platform boundary; tests substitute a protector without touching an OS store.
    internal static Func<string, string>? WindowsProtect = OperatingSystem.IsWindows() ? ProtectWindows : null;

    [SupportedOSPlatform("windows")]
    private static string ProtectWindows(string key) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));

    internal static string? Store(string? key)
    {
        if (WindowsProtect is { } protect)
        {
            try { return string.IsNullOrEmpty(key) ? "" : protect(key); }
            catch { Serilog.Log.Warning("Could not protect the companion API key"); return null; }
        }
        SecretStore.Store(Slot, key);
        return string.IsNullOrEmpty(key) ? "" : Reference;
    }

    internal static string? Read(string stored)
    {
        if (stored == Reference) return SecretStore.Retrieve(Slot);
        if (string.IsNullOrEmpty(stored)) return stored;
        byte[] data;
        try { data = Convert.FromBase64String(stored); }
        catch (FormatException)
        {
            // WPF SecureStringHelper.cs:47-51 accepts legacy plaintext. Migrate only the
            // current setting, never replace it because an old in-flight request read a key.
            var prompt = CoreSettings.Current?.CompanionPrompt;
            var persisted = prompt?.OpenAiCompatibleApiKey != stored ? null
                : WindowsProtect is not null ? Store(stored)
                : SecretStore.TryStore(Slot, stored) ? Reference : null;
            if (prompt?.OpenAiCompatibleApiKey == stored && persisted is not null)
            {
                prompt.OpenAiCompatibleApiKey = persisted;
                CoreSettings.Save();
            }
            return stored;
        }
        // WPF SecureStringHelper.cs:43-45: encrypted blobs use DPAPI with no entropy.
        // A valid base64 blob that cannot be decrypted must not become a Bearer token.
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }
}
