using System;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The tab / rack scoring maps, moved from WPF EmiTargets into <see cref="EmiDoors"/>
/// (were EmiRingCatalogueTests' reflection over the WPF private fields).</summary>
public sealed class EmiDoorsTests
{
    [Fact]
    public void Tab_and_rack_maps_only_point_at_real_targets()
    {
        // A typo here never fails a compile: the door just never scores, forever.
        var ids = EmiDoors.All.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var kv in EmiDoors.TabTargets)
            Assert.True(ids.Contains(kv.Value), $"tab \"{kv.Key}\" scores unknown target \"{kv.Value}\"");
        foreach (var kv in EmiDoors.RackTargets)
            Assert.True(ids.Contains(kv.Value), $"rack \"{kv.Key}\" scores unknown target \"{kv.Value}\"");
    }

    [Fact]
    public void No_door_is_counted_twice()
    {
        // A target is reachable by a tab OR a rack module OR a host launcher, never two of them,
        // or one sitting scores two opens and outranks an honest habit.
        var tabs = EmiDoors.TabTargets.Values.ToList();
        var racks = EmiDoors.RackTargets.Values.ToList();
        Assert.Empty(tabs.Intersect(racks, StringComparer.Ordinal));
        Assert.Equal(tabs.Count, tabs.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(racks.Count, racks.Distinct(StringComparer.Ordinal).Count());

        // The three ShowTab keys intercepted above the counter, plus the quiz landing page, are
        // counted at their launchers instead, so they must NOT also sit in the tab map.
        foreach (var intercepted in new[] { "fyp", "justdrop", "patreon", "gradedintake" })
            Assert.False(EmiDoors.TabTargets.ContainsKey(intercepted),
                $"\"{intercepted}\" is counted at its launcher, not at the bottom of ShowTab");
    }

    [Fact]
    public void Maps_match_keys_case_insensitively_and_the_studio_module_scores_loom()
    {
        Assert.Equal("sessions", EmiDoors.TabTargets["Presets"]);
        Assert.Equal("loom", EmiDoors.RackTargets["SPIRAL"]);
        Assert.False(EmiDoors.TabTargets.ContainsKey("studio"));
    }
}
