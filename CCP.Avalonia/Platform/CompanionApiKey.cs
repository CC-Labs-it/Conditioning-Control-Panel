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
        // Existing WPF profiles contain a DPAPI blob with no entropy (SecureStringHelper.cs:45-46).
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(stored)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }
}
