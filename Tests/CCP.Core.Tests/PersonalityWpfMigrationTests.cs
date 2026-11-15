using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public class PersonalityWpfMigrationTests
{
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
