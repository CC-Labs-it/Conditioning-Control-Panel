using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WorkshopTriggerEditorTests
{
    [Fact]
    public void EditorFiltersEmptyAndDuplicateInputs()
    {
        var data = WorkshopAccordion.TriggerEditorData(new[] { "", " ", "Obey", "OBEY", "Kneel" });
        Assert.Equal(2, data.Count);
        Assert.True(data["Obey"]);
        Assert.Empty(WorkshopAccordion.TriggerEditorData(null));
    }

    [Fact]
    public void SaveKeepsEnabledAndTracksOnlyNewNonDefaults()
    {
        var settings = new AppSettings();
        settings.UserAddedCustomTriggers.Add("removed");
        var old = WorkshopAccordion.TriggerEditorData(new[] { "removed", "kept" });
        var result = new Dictionary<string, bool> { ["kept"] = true, ["removed"] = false,
            ["default"] = true, ["new"] = true, ["disabled"] = false };
        WorkshopAccordion.ApplyTriggerEditorData(settings, old, result, new[] { "DEFAULT" });
        Assert.Equal(new[] { "kept", "default", "new" }, settings.CustomTriggers);
        Assert.Equal(new[] { "new" }, settings.UserAddedCustomTriggers);
    }
}
