using System;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF MainWindow.CompanionRoom.cs:312-321 and App.xaml.cs:659.
/// Settings hold a reference; the existing OS store owns the key. No plaintext disk fallback.</summary>
internal static class CompanionApiKey
{
    internal const string Slot = "companion_byo_key";
    internal const string Reference = "ccp-secret:companion-byo-v1";

    internal static void Seed() => OpenAiCompatibleService.ApiKeyUnprotect = Read;

    internal static string Store(string? key)
    {
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
            if (prompt?.OpenAiCompatibleApiKey == stored)
            {
                prompt.OpenAiCompatibleApiKey = Store(stored);
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
