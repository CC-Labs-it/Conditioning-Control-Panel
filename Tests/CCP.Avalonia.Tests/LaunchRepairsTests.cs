using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class LaunchRepairsTests
{
    [Fact]
    public void RepairsCustomSourcesAndReinstallsQueuedPresets()
    {
        var settings = new SettingsService();
        var previous = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            var custom = new KeywordTriggerPreset { Id = "custom-test", IsBuiltIn = false, MasterEnabled = true };
            custom.Triggers.Add(new KeywordTrigger { Id = "old", Keyword = "old" });
            settings.Current.KeywordTriggerPresets.Add(custom);
            settings.Current.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:custom-test:edited", Keyword = "edited" });
            var builtIn = new KeywordTriggerPreset { Id = "builtin-test", IsBuiltIn = true };
            builtIn.Triggers.Add(new KeywordTrigger { Id = "new", Keyword = "new", Actions = { new ChasterAddTimeAction { Enabled = true } } });
            settings.Current.KeywordTriggerPresets.Add(builtIn);
            settings.Current.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:builtin-test:old", Actions = { new ChasterAddTimeAction { Enabled = true } } });
            settings.PendingPresetReinstalls.Add(builtIn.Id);
            AvApp.RepairLaunchSettings(settings);
            Assert.Equal("edited", Assert.Single(custom.Triggers).Keyword);
            var clone = Assert.Single(settings.Current.KeywordTriggers.Where(t => t.Id == "preset:builtin-test:new"));
            Assert.True(Assert.IsType<ChasterAddTimeAction>(Assert.Single(clone.Actions)).Enabled);
            Assert.Empty(settings.PendingPresetReinstalls);
        }
        finally { CoreSettings.ServiceProvider = previous; }
    }
}
