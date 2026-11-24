using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Views.Controls;
using ConditioningControlPanel.Views.Controls.Studio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep, lane R3 (2026-11-24): Spiral (+ the Whirlpool box), Pink Filter (+ the Steam
/// box), Brain Drain (+ the Undertow box), Mind Wipe and the Deeper playlist card, each realized off
/// screen at 900 px with every fold open and its gold box rows shown, through the shared rows check
/// (emblem, OptionRow height, untrimmed label, no overlap, silence). With CCP_ROWS_DUMP set each
/// saves r3-&lt;page&gt;.png. No page here starts a player: the playlist card gets a fake service.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsR3Tests
{
    private static void Open(FrameworkElement page, string fold) => ((MoreFold)page.FindName(fold)!).IsOpen = true;

    [Fact]
    public void Spiral_rows_and_the_whirlpool_box_read() => WpfRenderHarness.OnStaThread(() =>
    {
        var labels = RowsRender.Check(new SpiralFeatureControl(), "r3-spiral", minLabels: 14, show: new[] { "PoolRows" });
        // The four Drags switches wear their effect's own picture and follow their switch.
        Assert.Equal(4, labels.Count(l => l.Parent is StackPanel { Orientation: Orientation.Horizontal }));
    });

    [Fact]
    public void Pink_filter_rows_and_the_steam_box_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new PinkFilterFeatureControl(), "r3-pinkfilter", minLabels: 11,
            show: new[] { "SteamRows", "CreepCard" }));

    [Fact]
    public void Brain_drain_rows_and_the_undertow_box_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new BrainDrainFeatureControl(), "r3-braindrain", minLabels: 10,
            show: new[] { "UndertowRows" }, before: p => Open(p, "FoldMore")));

    [Fact]
    public void Mind_wipe_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new MindWipeFeatureControl(), "r3-mindwipe", minLabels: 3));

    [Fact]
    public void Playlist_card_rows_read() => WpfRenderHarness.OnStaThread(() =>
    {
        var fake = new AudioPlaylistCardModelTests.FakePlaylist();
        fake.Add(new[] { @"C:\a\one.mp3", @"C:\a\two.flac" });
        var card = new AudioPlaylistCard();
        card.Use(fake);
        card.Model.Attach();
        RowsRender.Check(card, "r3-playlist", minLabels: 3, before: p => Open(p, "OptionsFold"));
    });
}
