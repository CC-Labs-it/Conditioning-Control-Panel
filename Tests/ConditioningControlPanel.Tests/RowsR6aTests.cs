using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep, lane R6a (2026-11-24): the Haptics, Takeover, Remote, Lockdown and Chaster
/// tabs, each built by plain construction (no handler invoked, nothing clicked, no device, no
/// network), every Expander opened and its hidden option blocks shown, then run through the shared
/// rows check (emblem, OptionRow height, untrimmed label, no overlap, silence). With CCP_ROWS_DUMP
/// set each saves r6a-&lt;tab&gt;.png.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsR6aTests
{
    /// <summary>Opens every Expander on the page: a look-only change, none of these tabs wires Expanded.</summary>
    private static void ExpandAll(FrameworkElement page)
    {
        foreach (var e in RowsRender.Descendants(page).OfType<Expander>().ToList()) e.IsExpanded = true;
    }

    [Fact]
    public void Haptics_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new HapticsTabView(), "r6a-haptics", minLabels: 21,
            show: new[] { "VideoHapticSyncSliders", "HapticAudioAdvanced" }, before: ExpandAll));

    [Fact]
    public void Takeover_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new BambiTakeoverTabView(), "r6a-takeover", minLabels: 27, before: ExpandAll));
}
