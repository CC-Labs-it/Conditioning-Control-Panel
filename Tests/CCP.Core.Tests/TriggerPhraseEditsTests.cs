using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TriggerPhraseEditsTests
{
    [Fact]
    public void EditorDropsBlankAndCaseDuplicateRowsWithoutSeedingAnEmptyPool()
    {
        Assert.Empty(TriggerPhraseEdits.ForEditor(null));
        Assert.Empty(TriggerPhraseEdits.ForEditor(new string[0]));
        var rows = TriggerPhraseEdits.ForEditor(new[] { "KEEP", "keep", " ", "", "other" });
        Assert.Equal(new[] { "KEEP", "other" }, rows.Keys);
        Assert.All(rows.Values, v => Assert.True(v));
    }

    [Fact]
    public void ApplyKeepsOnlyEnabledAndProtectsNewNonDefaultPhrasesFromModPruning()
    {
        var settings = new AppSettings { UserAddedCustomTriggers = new HashSet<string> { "removed", "kept" } };
        var before = new Dictionary<string, bool> { ["removed"] = true, ["kept"] = true };
        var edited = new Dictionary<string, bool>
        {
            ["removed"] = false, ["kept"] = true, ["my phrase"] = true,
            ["MOD DEFAULT"] = true, ["disabled new"] = false
        };
        TriggerPhraseEdits.Apply(settings, before, edited, new[] { "mod default" });
        Assert.Equal(new[] { "kept", "my phrase", "MOD DEFAULT" }, settings.CustomTriggers);
        Assert.Equal(2, settings.UserAddedCustomTriggers.Count);
        Assert.Contains("kept", settings.UserAddedCustomTriggers);
        Assert.Contains("MY PHRASE", settings.UserAddedCustomTriggers);
        Assert.DoesNotContain("removed", settings.UserAddedCustomTriggers);
        Assert.DoesNotContain("MOD DEFAULT", settings.UserAddedCustomTriggers);
    }

    [Fact]
    public void ClearingTheEditorRemovesHandTypedCustodyWithoutRestoringStockPhrases()
    {
        var settings = new AppSettings { UserAddedCustomTriggers = new HashSet<string> { "old" } };
        TriggerPhraseEdits.Apply(settings, new Dictionary<string, bool> { ["OLD"] = true }, new Dictionary<string, bool>(), null);
        Assert.Empty(settings.CustomTriggers);
        Assert.Empty(settings.UserAddedCustomTriggers);
    }
}
