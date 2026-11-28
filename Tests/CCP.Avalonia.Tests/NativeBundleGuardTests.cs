using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class NativeBundleGuardTests
{
    [Fact]
    public void HealthyProbeClearsStampAndContinues()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            NativeBundleRepair.WriteStamp(dir, "1", DateTime.UtcNow);
            Assert.True(NativeBundleGuard.VerifyOrRepairCore(null, "cache", "app", dir, "1",
                (_, _) => throw new Exception(), (_, _) => throw new Exception()));
            Assert.False(NativeBundleRepair.RepairedRecently(dir, "1", DateTime.UtcNow));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FailedProbeLaunchesOnceThenShowsGiveUp()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var failure = new DllNotFoundException();
            int launches = 0, dialogs = 0;
            bool Launch(string root, string exe) { Assert.Equal("cache", root); Assert.Equal("app", exe); launches++; return true; }
            void GiveUp(string? root, Exception ex) { Assert.Equal("cache", root); Assert.Same(failure, ex); dialogs++; }
            Assert.False(NativeBundleGuard.VerifyOrRepairCore(failure, "cache", "app", dir, "1", Launch, GiveUp));
            Assert.Equal(1, launches);
            Assert.Equal(0, dialogs);
            Assert.False(NativeBundleGuard.VerifyOrRepairCore(failure, "cache", "app", dir, "1", Launch, GiveUp));
            Assert.Equal(1, launches);
            Assert.Equal(1, dialogs);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DialogFailureStillStopsStartup()
    {
        Assert.False(NativeBundleGuard.VerifyOrRepairCore(new DllNotFoundException(), null, null,
            Path.GetTempPath(), "1", (_, _) => throw new Exception(), (_, _) => throw new Exception()));
    }

    [Fact]
    public void RecoveryDialogUsesSavedLanguageBeforeNormalStartup()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var settings = Path.Combine(dir, "settings.json");
        var legacy = Path.Combine(dir, "legacy.json");
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            File.WriteAllText(settings, "{\"nested\": {\"Language\": \"en\"}, \"Language\": \"fr\"}");
            var text = NativeBundleGuard.RecoveryText(settings, legacy, "CACHE-PATH", "FAILURE-DETAIL");
            Assert.Equal("Conditioning Control Panel - échec du démarrage", text.Title);
            Assert.Contains("CACHE-PATH", text.Message);
            Assert.Contains("FAILURE-DETAIL", text.Message);
            Assert.Contains("Fermez", text.Message);

            File.Delete(settings);
            File.WriteAllText(legacy, "{\"Language\":\"ja\"}");
            Assert.Equal("ja", NativeBundleGuard.ReadStartupLanguage(settings, legacy));
            File.WriteAllText(settings, "{");
            Assert.Equal("en", NativeBundleGuard.ReadStartupLanguage(settings, legacy));
            File.WriteAllText(settings, "{\"Language\":\"../bad\"}");
            Assert.Equal("en", NativeBundleGuard.ReadStartupLanguage(settings, legacy));

            foreach (var language in LocalizationManager.AvailableLanguages)
            {
                File.WriteAllText(settings, "{\"Language\":\"" + language.Code + "\"}");
                var localized = NativeBundleGuard.RecoveryText(settings, legacy, "CACHE-PATH", "FAILURE-DETAIL");
                Assert.NotEqual("native_repair_title", localized.Title);
                Assert.NotEqual("native_repair_message", localized.Message);
                Assert.Contains("CACHE-PATH", localized.Message);
                Assert.Contains("FAILURE-DETAIL", localized.Message);
                if (language.Code != "en") Assert.NotEqual("Conditioning Control Panel - startup failed", localized.Title);
            }
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage(oldLanguage);
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("cache", false)]
    public void MissingCacheOrFailedHelperShowsGiveUp(string? root, bool launchResult)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            int launches = 0, dialogs = 0;
            Assert.False(NativeBundleGuard.VerifyOrRepairCore(new DllNotFoundException(), root, "app", dir, "1",
                (_, _) => { launches++; return launchResult; }, (_, _) => dialogs++));
            Assert.Equal(root == null ? 0 : 1, launches);
            Assert.Equal(1, dialogs);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
