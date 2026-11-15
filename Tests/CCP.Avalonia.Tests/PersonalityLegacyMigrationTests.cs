using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    public Task DesktopStartupMigratesSavedPersonality(int scenario) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var snapshot = ProcessStateSnapshot.Take();
        var app = (App)Application.Current!;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
        var oldLifetime = app.ApplicationLifetime;
        var oldSettings = typeof(App).GetProperty(nameof(App.Settings))!.GetValue(null);
        var dispatchField = typeof(App).GetField("_desktopDispatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldDispatch = dispatchField.GetValue(app);
        var backupProperty = typeof(App).GetProperty("CloudAutoBackup", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldBackup = backupProperty.GetValue(null);
        var oldLedger = ConditioningControlPanel.Services.Awareness.AwarenessLive.Ledger;
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        try
        {
            var seed = new SettingsService();
            var s = seed.Current;
            s.ActivePersonalityPresetId = scenario == 2 ? "already-chosen" :
                scenario == 3 ? PersonalityPresets.BambiSpriteId : PersonalityPresets.NeutralDefaultId;
            s.SlutModeEnabled = scenario != 1;
            s.CompanionPrompt.UseCustomPrompt = scenario == 1 || scenario == 2;
            s.CompanionPrompt.Personality = "A saved personality";
            s.UserPersonalityPresets.Clear();
            seed.SaveImmediate(suppressCloudBackup: true);
            lifetimeField.SetValue(app, lifetime);
            var method = typeof(App).GetMethod("StartDesktop", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Func<double, string, Task> step = (progress, _) => progress == 0.4
                ? Task.FromException(new StopStartup()) : Task.CompletedTask;
            async Task<AppSettings> Launch()
            {
                (dispatchField.GetValue(app) as ConditioningControlPanel.Avalonia.AvaloniaCoreDispatch)?.Stop();
                App.Settings?.SaveImmediate(suppressCloudBackup: true); // the exit flush of the previous launch
                App.Settings?.SealForReset();
                var task = (Task)method.Invoke(app, new object[] { step })!;
                await Assert.ThrowsAsync<StopStartup>(() => task);
                return App.Settings!.Current;
            }
            var migrated = await Launch();
            Assert.True(migrated.PersonalityLegacyMigrated);
            if (scenario == 4)
            {
                // The legacy profile migrated once; now the user picks CCP Default with spice still on and
                // re-activates a community prompt. Later launches must keep both.
                Assert.Equal("slutmode", migrated.ActivePersonalityPresetId);
                migrated.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
                migrated.CompanionPrompt.UseCustomPrompt = true;
                for (var i = 0; i < 2; i++)
                {
                    migrated = await Launch();
                    Assert.Equal(PersonalityPresets.NeutralDefaultId, migrated.ActivePersonalityPresetId);
                    Assert.True(migrated.SlutModeEnabled);
                    Assert.True(migrated.CompanionPrompt.UseCustomPrompt);
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
            (dispatchField.GetValue(app) as ConditioningControlPanel.Avalonia.AvaloniaCoreDispatch)?.Stop();
            dispatchField.SetValue(app, oldDispatch);
            backupProperty.SetValue(null, oldBackup);
            ConditioningControlPanel.Services.Awareness.AwarenessLive.Ledger = oldLedger;
            App.Settings?.SealForReset();
            typeof(App).GetProperty(nameof(App.Settings))!.SetValue(null, oldSettings);
            lifetimeField.SetValue(app, oldLifetime);
            snapshot.Restore();
        }
    });

    private sealed class StopStartup : Exception { }
}
