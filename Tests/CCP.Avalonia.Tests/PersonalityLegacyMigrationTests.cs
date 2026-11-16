using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class PersonalityLegacyMigrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public Task DesktopStartupMigratesSavedPersonality(int scenario) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var snapshot = ProcessStateSnapshot.Take();
        var oldProvider = CoreSettings.ServiceProvider;
        SettingsService? service = null;
        try
        {
            var seed = new SettingsService();
            var s = seed.Current;
            s.PersonalityLegacyMigrated = false;
            s.ActivePersonalityPresetId = scenario == 2 ? "already-chosen" :
                scenario == 3 ? PersonalityPresets.BambiSpriteId : PersonalityPresets.NeutralDefaultId;
            s.SlutModeEnabled = scenario != 1;
            s.CompanionPrompt.UseCustomPrompt = scenario == 1 || scenario == 2;
            s.CompanionPrompt.Personality = "A saved personality";
            s.UserPersonalityPresets.Clear();
            seed.SaveImmediate(suppressCloudBackup: true);
            seed.SealForReset();
            // One simulated launch: the previous run's exit flush, then the settings load and the
            // startup migration exactly as StartDesktop runs them (wiring pinned by the test below).
            Task<AppSettings> Launch()
            {
                service?.SaveImmediate(suppressCloudBackup: true);
                service?.SealForReset();
                var loaded = new SettingsService();
                service = loaded;
                CoreSettings.ServiceProvider = () => loaded;
                App.MigrateLegacyPersonalityOnce(loaded);
                return Task.FromResult(loaded.Current);
            }
            var migrated = await Launch();
            Assert.True(migrated.PersonalityLegacyMigrated);
            if (scenario == 4 || scenario == 5 || scenario == 6)
            {
                // After migration the user picks a default with spice still on. In scenario 4 they
                // also re-activate a community prompt. Later launches must keep those choices.
                Assert.Equal("slutmode", migrated.ActivePersonalityPresetId);
                var chosen = scenario == 6 ? PersonalityPresets.BambiSpriteId : PersonalityPresets.NeutralDefaultId;
                migrated.ActivePersonalityPresetId = chosen;
                migrated.CompanionPrompt.UseCustomPrompt = scenario == 4;
                for (var i = 0; i < 2; i++)
                {
                    migrated = await Launch();
                    Assert.Equal(chosen, migrated.ActivePersonalityPresetId);
                    Assert.True(migrated.SlutModeEnabled);
                    Assert.Equal(scenario == 4, migrated.CompanionPrompt.UseCustomPrompt);
                    Assert.Empty(migrated.UserPersonalityPresets);
                }
            }
            if (scenario == 0 || scenario == 3) Assert.Equal("slutmode", migrated.ActivePersonalityPresetId);
            if (scenario == 1)
            {
                var preset = Assert.Single(migrated.UserPersonalityPresets);
                Assert.Equal("My Custom Personality", preset.Name);
                Assert.Equal("A saved personality", preset.PromptSettings.Personality);
                Assert.Equal(preset.Id, migrated.ActivePersonalityPresetId);
                Assert.False(migrated.CompanionPrompt.UseCustomPrompt);
            }
            if (scenario == 2)
            {
                Assert.Equal("already-chosen", migrated.ActivePersonalityPresetId);
                Assert.Empty(migrated.UserPersonalityPresets);
                Assert.True(migrated.CompanionPrompt.UseCustomPrompt);
            }
        }
        finally
        {
            service?.SealForReset();
            CoreSettings.ServiceProvider = oldProvider;
            snapshot.Restore();
        }
    });

    [Fact]
    public void DesktopStartupRunsTheMigrationRightAfterSettingsLoad()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        var start = src.IndexOf("private async Task StartDesktop(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var load = src.IndexOf("CoreSettings.ServiceProvider = () => Settings;", start, StringComparison.Ordinal);
        Assert.True(load > start);
        var call = src.IndexOf("MigrateLegacyPersonalityOnce(Settings);", start, StringComparison.Ordinal);
        Assert.True(call > load, "StartDesktop must run the legacy personality migration after settings load");
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
