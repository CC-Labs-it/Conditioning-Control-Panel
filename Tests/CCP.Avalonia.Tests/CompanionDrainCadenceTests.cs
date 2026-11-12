using System;
using System.IO;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public class CompanionDrainCadenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PerkAndCompanionChangesRestartOrStopDrain(int cleanupCheck) => AvaloniaTestDispatcher.Run(() =>
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var originalFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var oldHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        Func<CompanionId?> provider = () => CompanionId.BrainParasite;
        Action<CompanionId> switcher = _ => { };
        try
        {
            CoreModsHooks.ActiveCompanionProvider = provider;
            CoreModsHooks.SwitchCompanion = switcher;
            var service = RunScenario(path);
            if (cleanupCheck == 0) Assert.Same(provider, CoreModsHooks.ActiveCompanionProvider);
            else if (cleanupCheck == 1) Assert.Same(switcher, CoreModsHooks.SwitchCompanion);
            else if (cleanupCheck == 2) Assert.False(service.HasPendingSave);
            else if (originalFile == null) Assert.False(File.Exists(path));
            else Assert.Equal(originalFile, File.ReadAllBytes(path));
        }
        finally
        {
            (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = oldHooks;
        }
    });

    private static SettingsService RunScenario(string path)
    {
        var originalFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var oldHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        var oldEnv = Environment.GetEnvironmentVariable("CCP_COMPANION_V2");
        var oldProvider = CoreSettings.ServiceProvider;
        var wasStarted = CompanionHead.Started;
        SettingsService? service = null;
        try
        {
            if (wasStarted) CompanionHead.Stop();
            service = new SettingsService { SaveDebounceDueTimeMilliseconds = 600_000 };
            CoreSettings.ServiceProvider = () => service;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", "1");
            var s = CoreSettings.Current;
            s.PlayerLevel = 1000;
            s.CompanionPerk = CompanionBonusType.PinkFilterBonus;
            CompanionHead.Start();
            Assert.Null(CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.XPDrain));
            var first = Assert.IsAssignableFrom<IDisposable>(CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.XPDrain));
            var second = Assert.IsAssignableFrom<IDisposable>(CompanionHead.DrainTimer);
            Assert.NotSame(first, second);
            var next = CompanionCore.ActiveCompanion == CompanionId.CultBunny ? CompanionId.OGBambiSprite : CompanionId.CultBunny;
            CompanionCore.SwitchCompanion(next);
            Assert.NotSame(second, CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.PinkFilterBonus));
            Assert.Null(CompanionHead.DrainTimer);
            Assert.True(service.HasPendingSave);
            // Exercise file restoration even when the debounce has not fired.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "cadence test fixture");
            return service;
        }
        finally
        {
            CompanionHead.Stop();
            service?.SealForReset();
            try
            {
                if (originalFile == null) File.Delete(path);
                else File.WriteAllBytes(path, originalFile);
            }
            finally
            {
                CoreSettings.ServiceProvider = oldProvider;
                Environment.SetEnvironmentVariable("CCP_COMPANION_V2", oldEnv);
                try { if (wasStarted) CompanionHead.Start(); }
                finally { (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = oldHooks; }
            }
        }
    }
}
