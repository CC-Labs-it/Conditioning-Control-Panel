using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Found by the real-profile migration test: the bundled preset JSON has no action ids, so
/// every load minted new ones for every built-in preset and rewrote settings.json. The merge keeps
/// the stored ids where trigger id and action position and type match.</summary>
public sealed class BuiltInPresetActionIdTests
{
    private const string BundledJson = """
        { "id": "builtin.test", "name": "Test", "version": 2,
          "triggers": [ { "Id": "t1", "Keyword": "smart",
            "Actions": [ { "type": "PlayAudio" }, { "type": "VisualEffect" } ] } ] }
        """;

    private static KeywordTriggerPreset Bundled() => JsonConvert.DeserializeObject<KeywordTriggerPreset>(BundledJson)!;

    [Fact]
    public void ReloadKeepsTheStoredActionIds()
    {
        var settings = new AppSettings();
        settings.KeywordTriggerPresets.Clear();
        SettingsService.MergeBuiltInPresetInto(settings, Bundled(), new List<string>());
        var stored = settings.KeywordTriggerPresets[0].Triggers[0].Actions;
        var ids = new[] { stored[0].Id, stored[1].Id };

        var next = Bundled();
        Assert.NotEqual(ids[0], next.Triggers[0].Actions[0].Id);   // the bundle mints fresh ids
        SettingsService.MergeBuiltInPresetInto(settings, next, new List<string>());

        var after = settings.KeywordTriggerPresets[0].Triggers[0].Actions;
        Assert.Equal(ids, new[] { after[0].Id, after[1].Id });
    }

    [Fact]
    public void AChangedActionTypeGetsTheBundledId()
    {
        var settings = new AppSettings();
        settings.KeywordTriggerPresets.Clear();
        SettingsService.MergeBuiltInPresetInto(settings, Bundled(), new List<string>());
        var oldFirst = settings.KeywordTriggerPresets[0].Triggers[0].Actions[0].Id;

        var next = JsonConvert.DeserializeObject<KeywordTriggerPreset>(
            BundledJson.Replace("\"PlayAudio\"", "\"Highlight\""))!;
        var mintedFirst = next.Triggers[0].Actions[0].Id;
        SettingsService.MergeBuiltInPresetInto(settings, next, new List<string>());

        var after = settings.KeywordTriggerPresets[0].Triggers[0].Actions[0];
        Assert.Equal(mintedFirst, after.Id);
        Assert.NotEqual(oldFirst, after.Id);
    }
}
