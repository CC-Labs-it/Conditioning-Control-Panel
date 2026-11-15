using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public class PersonalityWpfMigrationTests
{
    [Fact]
    public void WpfStillSavesUnchangedDefaultsWhileTheOptOutDoesNot()
    {
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = 60000 };
        try
        {
            CoreSettings.ServiceProvider = () => service;
            service.Current.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
            service.Current.SlutModeEnabled = false;
            service.Current.CompanionPrompt.UseCustomPrompt = false;
            var personality = new PersonalityService();
            personality.MigrateFromLegacy(service.Current, saveUnchanged: false);
            Assert.False(service.HasPendingSave);
            personality.MigrateFromLegacy(service.Current);
            Assert.True(service.HasPendingSave);
        }
        finally
        {
            service.SealForReset();
            CoreSettings.ServiceProvider = previous;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WpfMigrationIgnoresAvaloniaLatch(bool customPrompt)
    {
        var previous = CoreSettings.ServiceProvider;
        try
        {
            CoreSettings.ServiceProvider = null;
            var settings = new AppSettings
            {
                PersonalityLegacyMigrated = true,
                ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId,
                SlutModeEnabled = !customPrompt
            };
            settings.CompanionPrompt.UseCustomPrompt = customPrompt;
            settings.CompanionPrompt.Personality = "saved prompt";
            new PersonalityService().MigrateFromLegacy(settings);
            if (customPrompt)
            {
                var preset = Assert.Single(settings.UserPersonalityPresets);
                Assert.Equal("saved prompt", preset.PromptSettings!.Personality);
                Assert.Equal(preset.Id, settings.ActivePersonalityPresetId);
                Assert.False(settings.CompanionPrompt.UseCustomPrompt);
            }
            else Assert.Equal(PersonalityPresets.SlutModeId, settings.ActivePersonalityPresetId);
        }
        finally { CoreSettings.ServiceProvider = previous; }
    }
}
