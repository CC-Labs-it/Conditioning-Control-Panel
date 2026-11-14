using System;
using System.IO;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Restack b177 (#3945): WPF BrainDrainService.Intensity and SessionEngine.cs:874-886. A session ramp
/// drives the running player without touching the saved rate; with no ramp the player follows the saved rate.</summary>
public sealed class BrainDrainSessionRampTests
{
    [Fact]
    public Task SessionRampDrivesTheRunningPlayerAndNeverTheSavedRate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ShellTestPlatform.Ensure();
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(root, "braindrain")).FullName, "clip.wav"), "");
        var prepared = 0;
        var player = new BrainDrainPlayer((_, _) => { prepared++; return null; }, new BrainDrainAudioTests.Clock(), () => 0.0001);
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CorePaths.EffectiveAssetsProvider = () => root;
            service.Current.BrainDrainEnabled = true; service.Current.BrainDrainIntensity = 0;
            player.SetIntensity(100); // ignored while stopped
            player.Start();
            Assert.Null(player.RampIntensity);
            player.Tick(TimeSpan.FromSeconds(5));
            Assert.Equal(0, prepared);
            player.SetIntensity(100);
            player.Tick(TimeSpan.FromSeconds(5));
            Assert.Equal(1, prepared);
            Assert.Equal(0, service.Current.BrainDrainIntensity);
            player.Stop(); player.Start(); // a fresh start follows the saved rate again
            Assert.Null(player.RampIntensity);
        }
        finally
        {
            player.Stop(); state.Dispose(); Directory.Delete(root, true);
            if (saved is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, saved);
        }
        return Task.CompletedTask;
    });
}
